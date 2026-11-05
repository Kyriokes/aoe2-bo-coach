using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// En pausa el juego oscurece todo ×0,2. Con el panel opaco del mod (fondo 24, 21, 18) el negro puro no aparece y la caja
/// no se estiraba: sin reloj, a los 30 s el coach daba la partida por terminada, cerraba el registro y abría el informe.
/// </summary>
public class ClockPausedTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    private static CapturedImage Paused(CapturedImage image)
    {
        var bgra = new byte[image.Bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                bgra[i + c] = (byte)Math.Round(image.Bgra[i + c] * 0.2);
            bgra[i + 3] = 255;
        }
        return new CapturedImage(image.Width, image.Height, bgra);
    }

    private static string? Clock(CapturedImage image)
    {
        var reading = Reader.Read(region => region == RegionNames.Clock ? image : null);
        return reading.Clock is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : null;
    }

    [Theory]
    [InlineData("clock-tapado-20261101-165020", "002012", "00:27:00")] // panel opaco
    [InlineData("clock-sin-fondo-20261028-232628", "000150", "00:01:21")] // textura detrás
    [InlineData("hud-20261025-024356", "000080", "00:00:24")] // fondo negro del mod
    [InlineData("rec-20261015-214512", "000256", "00:03:20")] // sin mod
    public void En_pausa_se_lee_la_misma_hora(string fixture, string frame, string expected)
    {
        var image = Png.Read(Path.Combine(Fixtures, fixture, $"{frame}_clock.png"));

        Assert.Equal(expected, Clock(image));
        Assert.Equal(expected, Clock(Paused(image)));
    }

    [Theory]
    [InlineData("001996")]
    [InlineData("001998")]
    public void Un_reloj_tapado_a_medias_sigue_sin_ser_una_hora_en_pausa(string frame)
    {
        var image = Png.Read(Path.Combine(Fixtures, "clock-tapado-20261101-165020", $"{frame}_clock.png"));

        Assert.Null(Clock(Paused(image)));
    }

    // Toda la grabación con reloj que hay: oscurecida ×0,2 da lo mismo que a la vista.
    [Theory]
    [InlineData("clock-tapado-20261101-165020")]
    [InlineData("clock-sin-fondo-20261028-232628")]
    [InlineData("hud-20261025-024356")]
    [InlineData("rec-20261015-205040")]
    [InlineData("rec-20261015-211049")]
    [InlineData("rec-20261015-214512")]
    public void Oscurecer_no_cambia_ninguna_lectura(string fixture)
    {
        var images = Directory.GetFiles(Path.Combine(Fixtures, fixture), "*_clock.png").OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(images);
        foreach (var path in images)
        {
            var image = Png.Read(path);
            if (Clock(image) is { } clock)
                Assert.True(clock == Clock(Paused(image)), $"{Path.GetFileName(path)}: a la vista {clock}, en pausa {Clock(Paused(image))}");
        }
    }
}
