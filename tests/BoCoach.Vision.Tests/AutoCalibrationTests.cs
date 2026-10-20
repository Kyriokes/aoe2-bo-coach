using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Calibración sola (hoja de ruta B4): con el mod, los cuadrados negros con franja de color y el rectángulo negro del
/// reloj son anclas que se encuentran en cualquier resolución. No hay capturas de pantalla entera, así que se arma una:
/// fondo liso y cada caja de la grabación 20261018-132830 pegada donde la calibración manual la tenía (session.json).
/// </summary>
public class AutoCalibrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261018-132830");

    private static readonly string[] Anchored = [RegionNames.Wood, RegionNames.Food, RegionNames.Gold, RegionNames.Stone, RegionNames.Population, RegionNames.IdleVillagers, RegionNames.Clock];

    /// <summary>Las cajas en píxeles de la calibración con que se grabó.</summary>
    private static Dictionary<string, PixelRect> RecordedBoxes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture, "session.json")));
        return doc.RootElement.GetProperty("regions").EnumerateObject().ToDictionary(
            r => r.Name,
            r => { var p = r.Value.GetProperty("pixels").EnumerateArray().Select(v => v.GetInt32()).ToArray(); return new PixelRect(p[0], p[1], p[2], p[3]); });
    }

    /// <summary>Una pantalla de 1920×1080 con el fondo del panel y las cajas grabadas (frame 100) en su lugar.</summary>
    internal static CapturedImage Composite(Dictionary<string, PixelRect> boxes)
    {
        const int w = 1920, h = 1080;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < bgra.Length; i += 4)
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (52, 58, 70, 255); // marrón del panel, nunca negro puro
        foreach (var (name, box) in boxes)
        {
            var path = Path.Combine(Fixture, $"000100_{name}.png");
            if (!File.Exists(path))
                continue;
            var image = Png.Read(path);
            for (var y = 0; y < image.Height; y++)
                Buffer.BlockCopy(image.Bgra, y * image.Width * 4, bgra, ((box.Y + y) * w + box.X) * 4, image.Width * 4);
        }
        return new CapturedImage(w, h, bgra);
    }

    private static void AssertClose(PixelRect expected, PixelRect actual, int tolerance, string what)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance
            && Math.Abs(expected.X + expected.Width - actual.X - actual.Width) <= tolerance
            && Math.Abs(expected.Y + expected.Height - actual.Y - actual.Height) <= tolerance,
            $"{what}: se esperaba {expected} y se encontró {actual}");
    }

    [Fact]
    public void Encuentra_los_seis_cuadrados_y_el_reloj_y_arma_las_mismas_cajas_que_la_calibracion_manual()
    {
        var boxes = RecordedBoxes();
        var screen = Composite(boxes);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        output.WriteLine($"escala {found.Scale:0.000} · cuadrados {string.Join(" ", found.Squares)} · reloj {found.Clock}");
        Assert.Equal(6, found.Squares.Count);
        Assert.Equal(1.0, found.Scale, 0.05);
        foreach (var name in Anchored)
        {
            var actual = found.Layout.PixelsOf(name, screen.Width, screen.Height);
            output.WriteLine($"{name,-15} manual {boxes[name]}  sola {actual}");
            AssertClose(boxes[name], actual, 3, name);
        }
        // Las demás regiones también están (el log, la edad, la cola), aunque no tengan ancla.
        Assert.All(RegionNames.All, name => Assert.False(found.Layout.PixelsOf(name, screen.Width, screen.Height).IsEmpty));
    }

    [Fact]
    public void A_doble_resolucion_encuentra_lo_mismo_al_doble()
    {
        var boxes = RecordedBoxes();
        var screen = Composite(boxes).Scale(2);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        Assert.Equal(2.0, found.Scale, 0.1);
        foreach (var name in Anchored)
        {
            var box = boxes[name];
            var expected = new PixelRect(box.X * 2, box.Y * 2, box.Width * 2, box.Height * 2);
            AssertClose(expected, found.Layout.PixelsOf(name, screen.Width, screen.Height), 6, name);
        }
    }

    // Captura real de un amigo (2026-10-20, 1920×1080, juego en castellano, HUD un 10 % más grande y panel negro, el
    // de otra civ): las franjas de color siguen ahí aunque el negro de los cuadrados se confunda con el panel.
    [Fact]
    public void Con_el_panel_negro_de_otra_civ_y_el_HUD_mas_grande_encuentra_las_franjas_y_el_reloj()
    {
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "amigo-20261020-1080p-panel-negro.png"));

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        output.WriteLine($"escala {found.Scale:0.000} · cuadrados {string.Join(" ", found.Squares)}");
        foreach (var name in RegionNames.All)
            output.WriteLine($"{name,-17} {found.Layout.PixelsOf(name, screen.Width, screen.Height)}");
        Assert.Equal(6, found.Squares.Count);
        Assert.All(found.Squares, s => Assert.InRange(s.Width, 37, 42));
        Assert.InRange(found.Scale, 1.0, 1.2);
        var wood = found.Layout.PixelsOf(RegionNames.Wood, screen.Width, screen.Height);
        Assert.InRange(wood.X, 4, 9);
        Assert.InRange(wood.Y, 4, 8);
        var clock = found.Layout.PixelsOf(RegionNames.Clock, screen.Width, screen.Height);
        Assert.InRange(clock.X, 840, 856); // "00:04:15" empieza a los 849 px
        var age = found.Layout.PixelsOf(RegionNames.Age, screen.Width, screen.Height);
        Assert.InRange(age.X, 640, 665); // "Alta Edad Media" empieza a los 656 px (el medallón de la edad, antes, no cuenta)
        Assert.True(age.X + age.Width < clock.X);
    }

    [Fact]
    public void Sin_el_mod_no_calibra()
    {
        var boxes = RecordedBoxes();
        // El reloj solo (un rectángulo negro) no alcanza: hacen falta los cuadrados con franja.
        var screen = Composite(boxes.Where(b => b.Key == RegionNames.Clock).ToDictionary());

        Assert.Null(AutoCalibration.Detect(screen, RegionLayout.Default));
    }
}
