using BoCoach.Vision.Digits;
using BoCoach.Vision.Recording;
using Xunit.Abstractions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Criterio de la Fase 7 sobre una grabación real (40 capturas al azar, valores leídos a ojo en golden.csv).
/// Los valores marcados con '*' estaban cortados por una caja de calibración demasiado chica: el lector tiene
/// que avisar que están cortados en vez de inventar un número.
/// </summary>
public class GoldenRecordingTests(ITestOutputHelper output)
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015");

    private static GlyphReader Reader(string font) => new(GlyphFont.Load(Path.Combine(AppContext.BaseDirectory, "fonts", $"{font}.json")));

    private static IEnumerable<(int Frame, Dictionary<string, string> Values)> Golden()
    {
        var lines = File.ReadAllLines(Path.Combine(Fixture, "golden.csv"));
        var header = lines[0].Split(',');
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            yield return (int.Parse(cells[0]), header.Skip(1).Zip(cells.Skip(1)).ToDictionary(p => p.First, p => p.Second));
        }
    }

    [Theory]
    [InlineData("clock", "clock", "clock")]
    [InlineData("wood", "wood", "stock")]
    [InlineData("food", "food", "stock")]
    [InlineData("gold", "gold", "stock")]
    [InlineData("stone", "stone", "stock")]
    [InlineData("population", "villagers", "villagers")]
    public void Lee_mas_del_98_por_ciento_y_avisa_los_numeros_cortados(string region, string column, string font)
    {
        var session = RecordedSession.Load(Fixture);
        var reader = Reader(font);
        int correct = 0, total = 0, clippedFlagged = 0, clippedTotal = 0;

        foreach (var (frame, values) in Golden())
        {
            var expected = values[column];
            var reading = reader.Read(session.Image(frame, region));
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
            if (reading.IsUsable && reading.Text == expected)
                correct++;
            else
                output.WriteLine($"{region} captura {frame}: esperaba {expected}, leyó {reading.Text} (seguridad {reading.Confidence:0.00}, cortado {reading.Clipped})");
        }

        output.WriteLine($"{region}: {correct}/{total} correctos, {clippedFlagged}/{clippedTotal} cortados detectados");
        Assert.True(correct >= Math.Ceiling(total * 0.98), $"{region}: {correct}/{total} correctos");
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
