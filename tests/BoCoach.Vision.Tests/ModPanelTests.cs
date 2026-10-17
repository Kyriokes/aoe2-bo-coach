using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

public class ModPanelTests
{
    // Caja de 60×20: panel gris, cuadrado negro (x 2..19) con un dígito blanco, panel, fondo negro del stock (x 24..55).
    private static CapturedImage Box(bool withStock = true)
    {
        const int w = 60, h = 20;
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var black = (x is >= 2 and <= 19) || (withStock && x is >= 24 and <= 55);
                var digit = (x is >= 9 and <= 11 && y is >= 10 and <= 17) || (x is >= 30 and <= 33 && y is >= 6 and <= 14);
                byte v = digit && black ? (byte)255 : black ? (byte)0 : (byte)120;
                var i = (y * w + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (v, v, v, 255);
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
        Assert.Equal(32, parts.Value.Stock!.Width);
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
