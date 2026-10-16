using BoCoach.Vision.Capture;
using BoCoach.Vision.Events;

namespace BoCoach.Vision.Tests;

public class LogImageTests
{
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);
    private static readonly (byte R, byte G, byte B) Grass = (120, 140, 70);
    private static readonly (byte R, byte G, byte B) DarkBox = (25, 25, 25);

    /// <summary>Imagen de pasto con las primeras <paramref name="textColumns"/> columnas de texto blanco.</summary>
    private static CapturedImage Map(int width, int height, int textColumns, double dim = 1)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = x < textColumns ? White : Grass;
                var i = (y * width + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = ((byte)(b * dim), (byte)(g * dim), (byte)(r * dim), 255);
            }
        }
        return new CapturedImage(width, height, bgra);
    }

    private static CapturedImage Pixels(params (byte R, byte G, byte B)[] colors)
    {
        var bgra = new byte[colors.Length * 4];
        for (var i = 0; i < colors.Length; i++)
            (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2], bgra[i * 4 + 3]) = (colors[i].B, colors[i].G, colors[i].R, 255);
        return new CapturedImage(colors.Length, 1, bgra);
    }

    [Fact]
    public void El_texto_blanco_queda_negro_y_el_mapa_y_la_caja_quedan_blancos()
    {
        var result = LogImage.WhiteText(Pixels(White, Grass, DarkBox, (150, 150, 150)));

        Assert.Equal(0, result.Pixel(0, 0).R);
        Assert.Equal(255, result.Pixel(1, 0).R);
        Assert.Equal(255, result.Pixel(2, 0).R);
        // Borde gris de una letra (antialiasing): tinta a medias.
        Assert.InRange(result.Pixel(3, 0).R, 40, 90);
    }

    [Fact]
    public void Con_el_juego_en_pausa_el_texto_oscurecido_se_lee_igual()
    {
        var normal = LogImage.WhiteText(Map(100, 10, 5).Stretched(LogImage.MaxGain));
        var paused = LogImage.WhiteText(Map(100, 10, 5, dim: 0.2).Stretched(LogImage.MaxGain));

        Assert.Equal(normal.Pixel(2, 5), paused.Pixel(2, 5));
        Assert.Equal(0, paused.Pixel(2, 5).R);
        Assert.Equal(255, paused.Pixel(50, 5).R);
    }

    [Fact]
    public void Una_captura_negra_no_inventa_texto()
    {
        var black = new CapturedImage(50, 10, Enumerable.Range(0, 50 * 10 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : 0)).ToArray());

        var prepared = LogImage.Prepare(black);

        Assert.All(Enumerable.Range(0, prepared.Width * prepared.Height), i => Assert.Equal(255, prepared.Bgra[i * 4]));
    }

    [Fact]
    public void Una_zona_gris_pareja_no_se_estira_como_si_fuera_texto_en_pausa()
    {
        var gray = new CapturedImage(50, 10, Enumerable.Range(0, 50 * 10 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : 30)).ToArray());

        Assert.Same(gray, gray.Stretched(LogImage.MaxGain));
        Assert.False(LogImage.HasText(gray));
    }

    [Fact]
    public void Agrega_el_margen_y_agranda()
    {
        var prepared = LogImage.Prepare(Map(40, 12, 3));

        Assert.Equal((40 + 2 * LogImage.Margin) * LogImage.Scale, prepared.Width);
        Assert.Equal((12 + 2 * LogImage.Margin) * LogImage.Scale, prepared.Height);
        Assert.Equal(255, prepared.Pixel(0, 0).R);
        Assert.Equal(0, prepared.Pixel((LogImage.Margin + 1) * LogImage.Scale, (LogImage.Margin + 5) * LogImage.Scale).R);
    }
}
