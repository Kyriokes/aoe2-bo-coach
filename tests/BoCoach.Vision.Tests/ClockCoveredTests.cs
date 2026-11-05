using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Partida 20261101-165020 (mod mínimo): a las 26:52 un cuadro del juego tapó el último dígito del reloj durante unos
/// segundos. La letra del mod leía "00:26:5::::" y la de siempre "00:26:5", que se tomaba por 26:05: el reloj volvía 47 s
/// para atrás, tres lecturas seguidas, y el coach empezaba otra partida (perdió 26 minutos). Un reloj a medias no es
/// una hora.
/// </summary>
public class ClockCoveredTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "clock-tapado-20261101-165020");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    [Theory]
    [InlineData("001996", null)]
    [InlineData("001998", null)]
    [InlineData("002012", "00:27:00")]
    public void Un_reloj_tapado_a_medias_no_se_lee_como_otra_hora(string frame, string? expected)
    {
        var image = Png.Read(Path.Combine(Fixture, $"{frame}_clock.png"));
        var reading = Reader.Read(region => region == RegionNames.Clock ? image : null);
        Assert.Equal(expected, reading.Clock is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : null);
    }

    // Un 0 leído como 9 en los minutos o los segundos no es una hora: "00:96:10" daba 01:36:10.
    [Theory]
    [InlineData("00:06:10", 370)]
    [InlineData("00:59:59", 3599)]
    [InlineData("01:00:00", 3600)]
    [InlineData("00:96:10", null)]
    [InlineData("00:06:70", null)]
    [InlineData("00:60:00", null)]
    public void Minutos_o_segundos_de_60_o_mas_no_son_una_hora(string text, int? seconds)
    {
        var fonts = HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts"));
        var image = HudLayoutScreens.ClockBox(text, fonts.ModClock!);

        Assert.Equal(seconds, Reader.Read(region => region == RegionNames.Clock ? image : null).Clock);
    }
}
