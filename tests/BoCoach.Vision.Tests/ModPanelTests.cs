using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

public class ModPanelTests
{
    // Caja de 60×20 sobre panel con textura: cuadrado (x 2..19) con franja de color arriba (y 0..6) y un dígito
    // blanco abajo, panel, fondo negro del stock (x 24..55) con su número.
    private static CapturedImage Box(bool withStock = true, bool withBand = true)
    {
        const int w = 60, h = 20;
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var inSquare = x is >= 2 and <= 19;
                var band = withBand && inSquare && y <= 6;
                var black = (inSquare && !band) || (withStock && x is >= 24 and <= 55);
                var digit = (x is >= 9 and <= 11 && y is >= 10 and <= 17) || (x is >= 30 and <= 33 && y is >= 6 and <= 14);
                var (r, g, b) = band ? (230, 102, 102)
                    : digit && black ? (255, 255, 255)
                    : black ? (0, 0, 0)
                    : (100 + (x * 7 + y * 13) % 60, 90, 80);
                var i = (y * w + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = ((byte)b, (byte)g, (byte)r, 255);
            }
        }
        return new CapturedImage(w, h, bgra);
    }

    [Fact]
    public void Separa_el_cuadrado_de_los_aldeanos_del_stock()
    {
        var parts = ModPanel.Split(Box());

        Assert.NotNull(parts);
        Assert.Equal(18, parts.Value.Square.Width);
        Assert.Equal(13, parts.Value.Square.Height); // sin la franja de color
        Assert.Equal(32, parts.Value.Stock!.Width);
    }

    [Fact]
    public void Un_fondo_negro_sin_la_franja_de_color_no_es_el_mod()
    {
        // El panel de KoBHV también tiene fondos negro puro detrás de los números.
        Assert.Null(ModPanel.Split(Box(withBand: false)));
    }

    [Fact]
    public void Inactivos_tiene_solo_el_cuadrado()
    {
        var parts = ModPanel.Split(Box(withStock: false));

        Assert.NotNull(parts);
        Assert.Null(parts.Value.Stock);
    }

    [Fact]
    public void Sin_el_mod_no_hay_cuadrado()
    {
        var bgra = Enumerable.Repeat((byte)120, 60 * 20 * 4).ToArray();

        Assert.Null(ModPanel.Split(new CapturedImage(60, 20, bgra)));
    }
}
