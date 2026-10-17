using BoCoach.Core.Data;
using BoCoach.Core.Engine;
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
    private readonly HudTracker _tracker = new();
    private readonly OcrReader? _ocr;
    private readonly EventLogReader? _log;
    private double _lastAgeRead = double.NegativeInfinity;

    /// <param name="ocr">OCR de Windows en inglés; sin él no se leen el log ni la edad.</param>
    public ScreenReader(HudFonts fonts, OcrReader? ocr, EventCatalog catalog)
    {
        _hud = new HudReader(fonts);
        _ocr = ocr;
        _log = ocr is null ? null : new EventLogReader(ocr, catalog);
    }

    /// <summary>
    /// Cada cuántos segundos se lee la edad. Cambia pocas veces por partida y el OCR es lo más caro, así que no
    /// hace falta leerla en cada captura.
    /// </summary>
    public double AgeEverySeconds { get; init; } = 2;

    /// <param name="regions">Imagen de cada región, o null si no está calibrada (o grabada).</param>
    /// <param name="realTime">Segundos reales desde que se empezó a leer.</param>
    public async Task<HudState> ReadAsync(Func<string, CapturedImage?> regions, double realTime)
    {
        var reading = _hud.Read(regions);

        IReadOnlyList<string> events = _log is not null && regions(RegionNames.EventLog) is { } log
            ? await _log.ReadAsync(log)
            : [];

        Age? age = null;
        if (_ocr is not null && realTime - _lastAgeRead >= AgeEverySeconds && regions(RegionNames.Age) is { } ageImage)
        {
            age = AgeText.Parse(await _ocr.ReadAsync(ageImage));
            _lastAgeRead = realTime;
        }

        return _tracker.Update(realTime, reading, events, age);
    }
}

public static class HudStateExtensions
{
    /// <summary>
    /// Lo que necesita el motor. La población "N/M" solo se lee con el mod BoCoach HUD; si no, la estima el
    /// SnapshotBuilder.
    /// </summary>
    public static ScreenReading ToReading(this HudState state) => new(
        state.Clock, state.Food, state.Wood, state.Gold, state.Stone, state.Villagers,
        state.Population, state.PopulationCap, state.NewEvents);
}
