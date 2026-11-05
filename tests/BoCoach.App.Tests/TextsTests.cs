using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using BoCoach.App;
using BoCoach.App.Coaching;
using BoCoach.Core;

namespace BoCoach.App.Tests;

/// <summary>Todos los textos de la app existen en los dos idiomas: se recorren por reflexión, con datos de relleno.</summary>
public partial class TextsTests
{
    /// <summary>Los que son iguales en los dos idiomas a propósito.</summary>
    private static readonly HashSet<string> SameInBoth =
    [
        nameof(Texts.AppName), nameof(Texts.No), nameof(Texts.StepBuildOrder), nameof(Texts.BuildOrderSection), nameof(Texts.Elo1v1Label),
        nameof(Texts.LiveError), nameof(Texts.SwitchReason), nameof(Texts.ModWarning), nameof(Texts.LanguageName), nameof(Texts.RegionLabel),
    ];

    [GeneratedRegex(@"[áéíóúñ¿¡]|\b(de|los|las|una|para|que|con|aldeanos?|partida|tocá|abrí|cuenta)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Spanish();

    private static IEnumerable<(string Name, Func<string> Read)> AllTexts()
    {
        foreach (var property in typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)))
            yield return (property.Name, () => (string)property.GetValue(null)!);
        foreach (var method in typeof(Texts).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
            yield return (method.Name, () => (string)method.Invoke(null, method.GetParameters().Select(Filler).ToArray())!);
    }

    private static object? Filler(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(int) || parameter.ParameterType == typeof(int?) ? 3
        : parameter.ParameterType == typeof(bool) ? true
        : parameter.ParameterType == typeof(DateTime) ? new DateTime(2026, 11, 3)
        : parameter.ParameterType.IsEnum ? Enum.GetValues(parameter.ParameterType).GetValue(0)
        : "X";

    [Fact]
    public void Cada_texto_cambia_con_el_idioma()
    {
        var same = new List<string>();
        foreach (var (name, read) in AllTexts().Where(t => !SameInBoth.Contains(t.Name)))
        {
            string spanish, english;
            using (Lang.Use(Lang.Spanish))
                spanish = read();
            using (Lang.Use(Lang.English))
                english = read();
            if (spanish == english)
                same.Add(name);
        }
        Assert.Empty(same);
    }

    [Fact]
    public void En_ingles_no_queda_nada_en_castellano()
    {
        using var english = Lang.Use(Lang.English);
        // diagnostico es el nombre de la carpeta y "DE" el del juego, que no cambian; cada idioma se nombra en el suyo.
        var spanish = AllTexts().Where(t => t.Name != nameof(Texts.LanguageName)).Select(t => (t.Name, Text: t.Read()))
            .Where(t => Spanish().IsMatch(t.Text.Replace("diagnostico", "").Replace(": DE", ""))).Select(t => $"{t.Name}: {t.Text}").ToList();
        Assert.Empty(spanish);
    }

    [Theory]
    [InlineData("es-AR", "es-AR", "es")]
    [InlineData("en-US", "es-AR", "es")] // Windows en inglés con el formato de su país
    [InlineData("es-ES", "en-US", "es")]
    [InlineData("en-US", "en-US", "en")]
    [InlineData("pt-BR", "pt-BR", "en")]
    public void Sin_elegir_sigue_a_Windows(string ui, string format, string expected) =>
        Assert.Equal(expected, CoachSettings.Detect(new CultureInfo(ui), new CultureInfo(format)));

    [Fact]
    public void Lo_elegido_se_guarda_y_manda_sobre_Windows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bocoach-lang-{Guid.NewGuid():N}.json");
        try
        {
            new CoachSettings { AppLanguage = Lang.English }.Save(path);
            Assert.Equal(Lang.English, CoachSettings.Load(path).EffectiveLanguage);
            File.WriteAllText(path, """{ "app_language": "klingon" }""");
            Assert.Equal("", CoachSettings.Load(path).AppLanguage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // IN-04: dice de quién es lo del juego, con el mismo sentido que el pie de la web, y no usa a Microsoft como respaldo.
    [Fact]
    public void El_aviso_del_contenido_del_juego_dice_de_quien_es_y_que_no_hay_respaldo()
    {
        Assert.Contains("son de Microsoft", Texts.GameContentNotice);
        Assert.Contains("Game Content Usage Rules", Texts.GameContentNotice);
        Assert.Contains("no está respaldado por Microsoft ni afiliado a Microsoft", Texts.GameContentNotice);
        using var english = Lang.Use(Lang.English);
        Assert.Contains("belong to Microsoft", Texts.GameContentNotice);
        Assert.Contains("Game Content Usage Rules", Texts.GameContentNotice);
        Assert.Contains("not endorsed by or affiliated with Microsoft", Texts.GameContentNotice);
    }

    [Fact]
    public void La_web_se_abre_en_el_idioma_de_la_app()
    {
        Assert.Equal("cuenta", Texts.WebPath("cuenta"));
        using var english = Lang.Use(Lang.English);
        Assert.Equal("en/cuenta", Texts.WebPath("cuenta"));
        Assert.Equal("en", Texts.WebPath(""));
    }
}
