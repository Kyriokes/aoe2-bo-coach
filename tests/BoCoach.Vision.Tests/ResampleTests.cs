using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Reescalar las regiones a 1080p pasa en cada captura fuera de esa resolución y el panel de puntajes se agranda cada 2 s:
/// lo que se ahorra en memoria no puede cambiar ni un byte, porque las letras y los íconos están medidos sobre esto.
/// </summary>
public class ResampleTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>El algoritmo tal como estaba al medir las letras y los íconos: dos pasadas, con un arreglo nuevo cada vez.</summary>
    private static (int Width, int Height, byte[] Bgra) Reference(CapturedImage image, double factor)
    {
        var w = Math.Max(1, (int)Math.Round(image.Width * factor));
        var h = Math.Max(1, (int)Math.Round(image.Height * factor));
        var columns = ReferenceWeights(image.Width, w);
        var rows = ReferenceWeights(image.Height, h);
        var horizontal = new double[image.Height * w * 3];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < w; x++)
                foreach (var (source, weight) in columns[x])
                    for (var c = 0; c < 3; c++)
                        horizontal[(y * w + x) * 3 + c] += image.Bgra[(y * image.Width + source) * 4 + c] * weight;
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var v = 0.0;
                    foreach (var (source, weight) in rows[y])
                        v += horizontal[(source * w + x) * 3 + c] * weight;
                    output[o + c] = (byte)Math.Clamp(Math.Round(v), 0, 255);
                }
                output[o + 3] = 255;
            }
        }
        return (w, h, output);
    }

    private static List<(int Source, double Weight)>[] ReferenceWeights(int from, int to)
    {
        var ratio = (double)from / to;
        var weights = new List<(int, double)>[to];
        for (var i = 0; i < to; i++)
        {
            var list = new List<(int, double)>();
            if (ratio > 1)
            {
                var start = i * ratio;
                var end = Math.Min(from, (i + 1) * ratio);
                for (var s = (int)start; s < end; s++)
                {
                    var cover = Math.Min(end, s + 1) - Math.Max(start, s);
                    if (cover > 1e-9)
                        list.Add((s, cover / (end - start)));
                }
            }
            else
            {
                var center = Math.Clamp((i + 0.5) * ratio - 0.5, 0, from - 1);
                var s0 = (int)center;
                var s1 = Math.Min(s0 + 1, from - 1);
                var f = center - s0;
                list.Add((s0, 1 - f));
                if (s1 != s0 && f > 1e-9)
                    list.Add((s1, f));
            }
            weights[i] = list;
        }
        return weights;
    }

    public static TheoryData<string, double> Images()
    {
        var data = new TheoryData<string, double>();
        foreach (var file in new[]
        {
            "scores/20261101-125353-001364.png",
            "scores/20261019-202635-000900.png",
            "hud-20261025-024356/000080_wood.png",
            "hud-20261025-024356/000080_clock.png",
            "clock-tapado-20261101-165020/002012_clock.png",
            "rec-20261015-214512/001399_event_log.png",
            "queue-20261018-021924/001060_production_queue.png",
        })
        {
            foreach (var factor in new[] { 1 / 1.0146, 1 / 1.09, 1 / 1.3333, 1 / 0.6667, 0.5, 2, 1.5 })
                data.Add(file, factor);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Reescalar_da_lo_mismo_byte_a_byte(string file, double factor)
    {
        var image = Png.Read(Path.Combine(Fixtures, file));

        var resampled = image.Resample(factor);
        var (width, height, bgra) = Reference(image, factor);

        Assert.Equal((width, height), (resampled.Width, resampled.Height));
        Assert.True(bgra.AsSpan().SequenceEqual(resampled.Bgra), $"{file} a {factor}");
    }

    // El arreglo de trabajo vuelve a usarse: lo que quedó de la imagen anterior no se puede sumar a la siguiente.
    [Fact]
    public void Dos_imagenes_seguidas_no_se_pisan()
    {
        var bright = new CapturedImage(200, 100, Enumerable.Repeat((byte)255, 200 * 100 * 4).ToArray());
        var dark = new CapturedImage(200, 100, new byte[200 * 100 * 4]);

        bright.Resample(1.5);
        var after = dark.Resample(1.5);

        Assert.True(Reference(dark, 1.5).Bgra.AsSpan().SequenceEqual(after.Bgra));
    }

    [Fact]
    public void Reescalar_no_reserva_el_arreglo_de_trabajo_en_cada_captura()
    {
        var image = new CapturedImage(400, 100, new byte[400 * 100 * 4]);
        image.Resample(1.5);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var output = image.Resample(1.5);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Lo que hay que reservar es la imagen de salida; los doubles de la pasada intermedia medirían 1,4 MB.
        Assert.InRange(allocated, output.Bgra.Length, output.Bgra.Length + 16 * 1024);
    }

    // El panel de puntajes se agranda ×2 cada 2 s: con Resample, 4,3 MB de doubles en el montículo de objetos grandes.
    public static TheoryData<string> Panels() => new(Directory.GetFiles(Path.Combine(Fixtures, "scores"), "*.png").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(Panels))]
    public void Agrandar_el_panel_de_puntajes_da_lo_mismo_que_reescalarlo(string file)
    {
        var panel = Png.Read(Path.Combine(Fixtures, "scores", file));

        var (width, height, bgra) = Reference(panel, 2);
        var smooth = panel.ScaleSmooth(2);

        Assert.Equal((width, height), (smooth.Width, smooth.Height));
        Assert.True(bgra.AsSpan().SequenceEqual(smooth.Bgra));
    }

    [Fact]
    public void Preparar_el_panel_de_puntajes_no_reserva_los_doubles()
    {
        var panel = Png.Read(Path.Combine(Fixtures, "scores", "20261101-125353-001364.png"));
        ScoresPanelReader.Prepare(panel);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var prepared = ScoresPanelReader.Prepare(panel);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // La imagen preparada, la del paso anterior y la del medio; los doubles medían tres veces lo que mide la preparada.
        Assert.True(allocated < prepared.Bgra.Length * 3, $"reservó {allocated} bytes y la imagen mide {prepared.Bgra.Length}");
    }
}
