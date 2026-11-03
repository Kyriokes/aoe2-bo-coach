namespace BoCoach.Core;

/// <summary>
/// El idioma de los textos que se le muestran al jugador. La app elige uno al arrancar (<see cref="SetDefault"/>) y la
/// web lo pide por llamada (<see cref="Use"/>, que no pisa al resto del proceso: por eso los tests pueden correr en
/// paralelo). Sin elegir nada es el castellano. No es el idioma del juego: ese dice cómo leer la pantalla.
/// </summary>
public static class Lang
{
    public const string Spanish = "es";
    public const string English = "en";

    /// <summary>Los idiomas que tienen textos (los mismos de la web).</summary>
    public static IReadOnlyList<string> All { get; } = [Spanish, English];

    private static volatile string _default = Spanish;
    private static readonly AsyncLocal<string?> Scoped = new();

    public static string Current => Scoped.Value ?? _default;

    public static bool IsEnglish => Current == English;

    /// <summary>Un idioma que no se conoce se muestra en castellano.</summary>
    public static string Known(string? language) => language == English ? English : Spanish;

    public static void SetDefault(string? language) => _default = Known(language);

    /// <summary>Hasta el Dispose, este flujo (y lo que espere con await) usa ese idioma.</summary>
    public static IDisposable Use(string? language)
    {
        var previous = Scoped.Value;
        Scoped.Value = Known(language);
        return new Restore(previous);
    }

    /// <summary>El texto en el idioma actual.</summary>
    public static string L(string spanish, string english) => IsEnglish ? english : spanish;

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
