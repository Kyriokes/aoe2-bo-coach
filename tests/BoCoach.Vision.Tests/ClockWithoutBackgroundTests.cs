using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Partida 20261028-232628 (después del parche del juego del 2026-10-27): el mod sigue ubicando el reloj y su letra de
/// 56 pt, pero ya no se dibuja el rectángulo negro detrás. La caja toma también " (Normal - 1.7) (Standard)" sobre la
/// textura del panel, y el coach no leía ningún reloj. Horas leídas a ojo.
/// </summary>
public class ClockWithoutBackgroundTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "clock-sin-fondo-20261028-232628");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    [Theory]
    [InlineData("000060", "00:00:09")]
    [InlineData("000150", "00:01:21")]
    [InlineData("000240", "00:02:25")]
    [InlineData("000260", "00:02:41")]
    public void Lee_el_reloj_del_mod_sin_el_fondo_negro(string frame, string expected)
    {
        var image = Png.Read(Path.Combine(Fixture, $"{frame}_clock.png"));
        var reading = Reader.Read(region => region == RegionNames.Clock ? image : null);
        Assert.Equal(expected, reading.Clock is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : null);
    }
}
