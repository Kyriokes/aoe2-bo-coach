using BoCoach.Vision.Digits;
using BoCoach.Vision.Recording;
using Xunit.Abstractions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Criterio de la Fase 7 sobre partidas reales: capturas al azar con los valores leídos a ojo en golden.csv.
/// <list type="bullet">
/// <item>rec-20261015-205040: mitad de partida, HUD marrón (40 capturas).</item>
/// <item>rec-20261015-214512: partida desde el arranque, HUD marrón (40 capturas).</item>
/// <item>rec-20261015-211049: partida desde el arranque, HUD gris de otra civ (20 capturas, 2 de pantalla de carga).</item>
/// </list>
/// '*' = el número estaba cortado por una caja chica (el lector tiene que avisarlo en vez de inventar);
/// '-' = no se ve nada (pantalla de carga): no tiene que leer ningún número.
/// </summary>
public class GoldenRecordingTests(ITestOutputHelper output)
{
    public static readonly string[] Fixtures = ["rec-20261015-205040", "rec-20261015-214512", "rec-20261015-211049"];

    private static string FixtureDir(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static GlyphReader Reader(string font) => new(GlyphFont.Load(Path.Combine(AppContext.BaseDirectory, "fonts", $"{font}.json")));

    private static IEnumerable<(int Frame, Dictionary<string, string> Values)> Golden(string fixture)
    {
        var lines = File.ReadAllLines(Path.Combine(FixtureDir(fixture), "golden.csv"));
        var header = lines[0].Split(',');
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            yield return (int.Parse(cells[0]), header.Skip(1).Zip(cells.Skip(1)).ToDictionary(p => p.First, p => p.Second));
        }
    }

    public static TheoryData<string, string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var fixture in Fixtures)
        {
            data.Add(fixture, "clock", "clock", "clock");
            data.Add(fixture, "wood", "wood", "stock");
            data.Add(fixture, "food", "food", "stock");
            data.Add(fixture, "gold", "gold", "stock");
            data.Add(fixture, "stone", "stone", "stock");
            data.Add(fixture, "population", "villagers", "villagers");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Lee_mas_del_98_por_ciento_y_avisa_los_numeros_cortados(string fixture, string region, string column, string font)
    {
        var session = RecordedSession.Load(FixtureDir(fixture));
        var reader = Reader(font);
        int correct = 0, total = 0, clippedFlagged = 0, clippedTotal = 0;

        foreach (var (frame, values) in Golden(fixture))
        {
            var expected = values[column];
            var image = session.Image(frame, region);
            var reading = region == "clock" ? reader.Read(image) : reader.ReadRightmostGroup(image);
            if (expected.EndsWith('*'))
            {
                clippedTotal++;
                if (reading.Clipped)
                    clippedFlagged++;
                else
                    output.WriteLine($"{region} captura {frame}: estaba cortado ({expected}) y leyó {reading.Text} sin avisar");
                continue;
            }

            total++;
            var ok = expected == "-"
                ? !reading.IsUsable
                : reading.IsUsable && reading.Text == expected;
            if (ok)
                correct++;
            else
                output.WriteLine($"{region} captura {frame}: esperaba {expected}, leyó {reading.Text} (seguridad {reading.Confidence:0.00}, cortado {reading.Clipped})");
        }

        output.WriteLine($"{fixture} {region}: {correct}/{total} correctos, {clippedFlagged}/{clippedTotal} cortados detectados");
        Assert.True(correct >= Math.Ceiling(total * 0.98), $"{fixture} {region}: {correct}/{total} correctos");
        Assert.Equal(clippedTotal, clippedFlagged);
    }

    [Fact]
    public void La_letra_del_reloj_se_guarda_y_se_carga_igual()
    {
        var font = GlyphFont.Load(Path.Combine(AppContext.BaseDirectory, "fonts", "clock.json"));
        var path = Path.Combine(Path.GetTempPath(), $"font-{Guid.NewGuid():N}.json");
        try
        {
            font.Save(path);
            var again = GlyphFont.Load(path);

            Assert.Equal(font.Chars, again.Chars);
            Assert.Equal(font.Templates.Count, again.Templates.Count);
            Assert.Equal("0123456789:", string.Concat(font.Chars));
            Assert.Equal(font.TypicalWidthRatio, again.TypicalWidthRatio, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
