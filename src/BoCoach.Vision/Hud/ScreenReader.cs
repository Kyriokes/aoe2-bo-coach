using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Record;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Events;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Hud;

/// <summary>
/// Lee una captura completa (todas las regiones) y la suaviza: números del HUD por plantillas, log de eventos
/// y edad por OCR. Es lo mismo en vivo y sobre una grabación; solo cambia de dónde salen las imágenes.
/// </summary>
public sealed class ScreenReader
{
    private readonly HudReader _hud;
    private readonly IconLibrary? _queueIcons;
    private readonly HudTracker _tracker = new();
    private readonly OcrReader? _ocr;
    private readonly EventLogReader? _log;
    private readonly ScoresPanelReader? _scores;
    private readonly HudLayout? _layout;
    private double _lastAgeRead = double.NegativeInfinity;
    private double _lastScoresRead = double.NegativeInfinity;

    /// <param name="ocr">OCR de Windows en inglés; sin él no se leen el log ni la edad.</param>
    /// <param name="layout">El diseño del mod nuevo con que se calibró o grabó (cada campo se recorta por él), o null.</param>
    /// <param name="captureIntervalMs">Cada cuánto llegan las capturas.</param>
    public ScreenReader(HudFonts fonts, OcrReader? ocr, EventCatalog catalog, HudLayout? layout = null, double captureIntervalMs = 500)
    {
        _layout = layout;
        _hud = new HudReader(fonts);
        _queueIcons = fonts.QueueIcons;
        _ocr = ocr;
        _log = ocr is null ? null : new EventLogReader(ocr, catalog, captureIntervalMs);
        _scores = ocr is null ? null : new ScoresPanelReader(ocr);
    }

    /// <summary>
    /// Cada cuántos segundos se lee la edad. Cambia pocas veces por partida y el OCR es lo más caro, así que no
    /// hace falta leerla en cada captura.
    /// </summary>
    public double AgeEverySeconds { get; init; } = 2;

    /// <summary>Lo que va al registro en vez de un texto de la edad que no se entendió: en un menú ahí puede haber cualquier cosa.</summary>
    public const string UnknownAge = "?";

    /// <summary>
    /// Lo crudo de la última captura, antes de suavizar (el registro de partida): con esto, <see cref="ScreenReplay"/>
    /// da el mismo estado sin las imágenes.
    /// </summary>
    public RawCapture? LastRaw { get; private set; }

    /// <param name="regions">Imagen de cada región, o null si no está calibrada (o grabada).</param>
    /// <param name="realTime">Segundos reales desde que se empezó a leer.</param>
    public async Task<HudState> ReadAsync(Func<string, CapturedImage?> regions, double realTime)
    {
        var reading = _hud.Read(regions, _layout);

        var logRead = _log is not null && regions(RegionNames.EventLog) is not null;
        IReadOnlyList<string> events = logRead ? await _log!.ReadAsync(regions(RegionNames.EventLog)!) : [];

        Age? age = null;
        string? ageText = null;
        if (_ocr is not null && realTime - _lastAgeRead >= AgeEverySeconds && regions(RegionNames.Age) is { } ageImage)
        {
            ageText = await _ocr.ReadAsync(ageImage);
            age = AgeText.Parse(ageText);
            ageText = age is null ? UnknownAge : ageText;
            _lastAgeRead = realTime;
        }

        // La cola de producción: íconos comparados con la biblioteca (rápido: unos pocos lugares de 11×11 colores).
        var queue = _queueIcons is not null && regions(RegionNames.ProductionQueue) is { } queueImage ? _queueIcons.Read(queueImage) : null;

        // El panel de puntajes, cada 2 s como la edad (D3: el clic de edad del rival se ve en su puntaje).
        IReadOnlyList<PlayerScore>? scores = null;
        if (_scores is not null && realTime - _lastScoresRead >= AgeEverySeconds && regions(RegionNames.Scores) is { } scoresImage)
        {
            scores = (await _scores.ReadAsync(scoresImage)).Where(r => r.Slot is not null)
                .Select(r => new PlayerScore(r.Slot!.Value, r.Score, r.DarkAge)).ToList();
            _lastScoresRead = realTime;
        }

        LastRaw = new RawCapture(reading.Clock, reading.Food, reading.Wood, reading.Gold, reading.Stone, reading.Villagers, reading.IdleVillagers,
            reading.Population, reading.PopulationCap, reading.VillagersOn, reading.Confidence, reading.Clipped,
            logRead ? _log!.LastLines : null, ageText, queue, scores);

        return _tracker.Update(realTime, reading, events, age) with { Queue = queue, Scores = scores };
    }
}

/// <summary>
/// Vuelve a leer las capturas de un registro de partida, sin imágenes: el mismo suavizado y el mismo log que
/// <see cref="ScreenReader"/>, partiendo de lo crudo.
/// </summary>
/// <param name="captureIntervalMs">Cada cuánto se miró la pantalla en esa partida (<c>interval_ms</c> del registro).</param>
public sealed class ScreenReplay(EventCatalog catalog, double captureIntervalMs = 500)
{
    private readonly HudTracker _tracker = new();
    private readonly EventLogReader _log = new(null, catalog, captureIntervalMs);

    public HudState Read(RawCapture raw, double realTime)
    {
        var reading = new HudReading(raw.Clock, raw.Food, raw.Wood, raw.Gold, raw.Stone, raw.Villagers, raw.Idle, raw.Clipped,
            raw.VillagersOn, raw.Population, raw.PopulationCap);
        var events = raw.LogLines is { } lines ? _log.Process(lines) : [];
        return _tracker.Update(realTime, reading, events, raw.AgeText is { } age ? AgeText.Parse(age) : null) with { Queue = raw.Queue, Scores = raw.Scores };
    }
}

public static class HudStateExtensions
{
    /// <summary>
    /// Lo que necesita el motor. La población "N/M" solo se lee con el mod BoCoach HUD; si no, la estima el
    /// SnapshotBuilder. Sin ninguna hora todavía, la cola no se pasa: una tecnología vista "en el 0:00" a mitad de partida
    /// parecería terminada apenas falte de la cola.
    /// </summary>
    public static ScreenReading ToReading(this HudState state) => new(
        state.Clock, state.Food, state.Wood, state.Gold, state.Stone, state.Villagers,
        state.Population, state.PopulationCap, state.NewEvents,
        state.VillagersOn is { Count: 4 } on
            ? new ResourceAmounts(on[Resource.Food], on[Resource.Wood], on[Resource.Stone], on[Resource.Gold])
            : null,
        state.Clock is null ? null : state.Queue,
        state.Scores,
        state.IdleVillagers,
        state.ClockChange);
}
