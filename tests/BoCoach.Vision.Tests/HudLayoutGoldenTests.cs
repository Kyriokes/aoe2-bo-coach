using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Recording;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Tercera prueba del HUD propio (grabación 20261025-024356, 2026-10-25): ocho capturas con los valores leídos a ojo
/// en golden.csv. Las letras hud_numbers (48 pt, con el "/") y mod_clock se armaron con esta grabación (mod_clock,
/// también con las del mod viejo).
/// </summary>
public class HudLayoutGoldenTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261025-024356");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    private static IEnumerable<Dictionary<string, string>> Golden()
    {
        var lines = File.ReadAllLines(Path.Combine(Fixture, "golden.csv"));
        var header = lines[0].Split(',');
        return lines.Skip(1).Where(l => l.Length > 0).Select(l => header.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second));
    }

    [Fact]
    public void Lee_el_golden_de_la_grabacion_con_el_diseno()
    {
        var session = RecordedSession.Load(Fixture);
        Assert.NotNull(session.Hud);
        var failures = Failures(session, (frame, region) => session.Has(frame, region) ? session.Image(frame, region) : null);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Las mismas capturas, con la GD adentro de los casilleros, llevadas a la escala de otras resoluciones y de vuelta a
    // 1080p como en vivo: el casillero se tiene que seguir reconociendo con el borde borroneado, sin mirar adentro de la
    // GD. El juego no se pudo probar a estas resoluciones; a 720p se acepta un error (los números miden 9 px).
    [Theory]
    [InlineData(2.0, 0)]
    [InlineData(1.5, 0)]
    [InlineData(1.3333, 0)]
    [InlineData(0.8333, 0)]
    [InlineData(0.7111, 0)]
    [InlineData(0.6667, 1)]
    public void Lee_el_golden_a_la_escala_de_otras_resoluciones(double scale, int allowed)
    {
        var session = RecordedSession.Load(Fixture);
        CapturedImage? Image(int frame, string region)
        {
            var path = Path.Combine(Fixture, SessionRecorder.FileName(frame, region));
            return File.Exists(path) ? Png.Read(path).Resample(scale).AtHudScale(scale) : null;
        }

        var failures = Failures(session, Image);

        Assert.True(failures.Count <= allowed, string.Join(Environment.NewLine, failures));
    }

    private static List<string> Failures(RecordedSession session, Func<int, string, CapturedImage?> image)
    {
        var failures = new List<string>();
        foreach (var expected in Golden())
        {
            var frame = int.Parse(expected["frame"]);
            var reading = Reader.Read(region => image(frame, region), session.Hud);
            void Check(string column, object? actual)
            {
                if (!string.Equals(expected[column], actual?.ToString(), StringComparison.Ordinal))
                    failures.Add($"captura {frame}, {column}: se esperaba {expected[column]} y se leyó {actual ?? "-"}");
            }
            Check("clock", reading.Clock is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : null);
            foreach (var (resource, name) in new[] { (Resource.Wood, "wood"), (Resource.Food, "food"), (Resource.Gold, "gold"), (Resource.Stone, "stone") })
            {
                Check($"{name}_villagers", reading.VillagersOn is { } on && on.TryGetValue(resource, out var v) ? v : null);
                Check($"{name}_stock", reading.Stock(resource));
            }
            Check("total", reading.Villagers);
            Check("cap", reading.Population is { } n && reading.PopulationCap is { } m ? $"{n}/{m}" : null);
            Check("idle", reading.IdleVillagers);
            if (reading.LayoutName != session.Hud!.Name)
                failures.Add($"captura {frame}: no se leyó con el diseño ({reading.LayoutName ?? "-"})");
        }
        return failures;
    }
}
