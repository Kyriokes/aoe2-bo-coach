using BoCoach.Core.Data;

namespace BoCoach.Vision.Ocr;

/// <summary>
/// Idioma en que está el juego: con qué OCR se leen el log y la edad, y qué frases del log (data/events.&lt;código&gt;.json)
/// se suman a las inglesas. Los ids de evento son los mismos en todos los idiomas.
/// </summary>
/// <param name="Code">Lo que se guarda en settings.json.</param>
/// <param name="Label">Lo que muestra el desplegable.</param>
/// <param name="OcrLanguages">Idiomas del OCR de Windows a probar, en orden; si no hay ninguno, el inglés.</param>
/// <param name="EventsFile">Archivo de frases del log además de events.json, o null (inglés).</param>
public sealed record GameLanguage(string Code, string Label, IReadOnlyList<string> OcrLanguages, string? EventsFile)
{
    public static GameLanguage English { get; } = new("en", "Inglés", ["en-US"], null);

    // Castellano de España (resources/es) y de Latinoamérica (resources/mx): las plantillas del log son iguales pero
    // muchos nombres cambian ("Caballero" es el caballero en uno y el caballero de choque en el otro).
    public static GameLanguage SpanishSpain { get; } = new("es", "Castellano (España)", ["es-ES", "es-MX", "es"], "events.es.json");
    public static GameLanguage SpanishLatinAmerica { get; } = new("mx", "Castellano (Latinoamérica)", ["es-MX", "es-ES", "es"], "events.mx.json");

    public static IReadOnlyList<GameLanguage> All { get; } = [English, SpanishSpain, SpanishLatinAmerica];

    /// <summary>El idioma con ese código, o el inglés si no hay ninguno (ajustes viejos o rotos).</summary>
    public static GameLanguage FromCode(string? code) => All.FirstOrDefault(l => l.Code == code?.Trim().ToLowerInvariant()) ?? English;

    /// <summary>
    /// El OCR del primer idioma de <see cref="OcrLanguages"/> que Windows tenga instalado, o el inglés. El inglés lee
    /// las frases en castellano sin los acentos y el reconocimiento del log lo tolera.
    /// </summary>
    public OcrReader? CreateOcr() =>
        OcrLanguages.Select(l => OcrReader.TryCreate(l)).FirstOrDefault(r => r is not null) ?? OcrReader.TryCreate();

    /// <summary>Las frases del log: las inglesas (events.json) más las del idioma si el archivo está.</summary>
    /// <param name="dataDir">Carpeta con events.json.</param>
    public EventCatalog LoadEvents(string dataDir, GameData data)
    {
        var events = EventCatalog.Load(Path.Combine(dataDir, "events.json"));
        if (EventsFile is not null && File.Exists(Path.Combine(dataDir, EventsFile)))
            events = events.Merged(EventCatalog.Load(Path.Combine(dataDir, EventsFile)));
        return events.PreferringEntitiesOf(data);
    }

    public override string ToString() => Label;
}
