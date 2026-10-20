using BoCoach.Core.Data;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Idioma del juego (amigos del usuario con el juego en castellano, 2026-10-20): elige el OCR y las frases del log
/// que se suman a las inglesas.
/// </summary>
public class GameLanguageTests
{
    private static readonly GameData Data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data"));

    [Theory]
    [InlineData("en", "en")]
    [InlineData("es", "es")]
    [InlineData("mx", "mx")]
    [InlineData("", "en")]       // ajustes viejos
    [InlineData(null, "en")]
    [InlineData("klingon", "en")]
    public void Se_elige_por_codigo_y_lo_desconocido_es_ingles(string? code, string expected)
    {
        Assert.Equal(expected, GameLanguage.FromCode(code).Code);
    }

    [Fact]
    public void En_castellano_el_log_tiene_las_frases_del_idioma_y_las_inglesas()
    {
        var mx = GameLanguage.FromCode("mx").LoadEvents(AppContext.BaseDirectory, Data);
        var en = GameLanguage.English.LoadEvents(AppContext.BaseDirectory, Data);

        Assert.Contains("Jinete: creación finalizada", mx.Phrases["knight_created"]);
        Assert.Contains("Knight Created", mx.Phrases["knight_created"]);
        Assert.DoesNotContain("Jinete: creación finalizada", en.Phrases["knight_created"]);
        Assert.True(mx.IsCommon("villager_created", mx.Phrases["villager_created"].ToList().IndexOf("Aldeano: creación finalizada")));
    }

    [Fact]
    public void Sin_el_archivo_del_idioma_queda_el_ingles()
    {
        var dir = Directory.CreateTempSubdirectory("bocoach-lang-");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "events.json"), Path.Combine(dir.FullName, "events.json"));

            var events = GameLanguage.FromCode("es").LoadEvents(dir.FullName, Data);

            Assert.Equal(["Villager Created"], events.Phrases["villager_created"]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void El_OCR_prueba_los_idiomas_del_juego_y_si_no_hay_ninguno_usa_el_ingles()
    {
        var installed = OcrReader.AvailableLanguages();
        if (installed.Count == 0)
            return; // sin ningún OCR instalado (puede pasar en CI)

        var reader = GameLanguage.FromCode("es").CreateOcr();

        Assert.NotNull(reader);
        var spanish = installed.FirstOrDefault(l => l.StartsWith("es", StringComparison.OrdinalIgnoreCase));
        if (spanish is not null)
            Assert.StartsWith("es", reader.Language, StringComparison.OrdinalIgnoreCase);
        else
            Assert.Equal("en-US", reader.Language);
    }
}
