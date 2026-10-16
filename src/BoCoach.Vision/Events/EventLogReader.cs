using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Events;

/// <summary>
/// Lee el log de eventos captura por captura y devuelve solo los eventos nuevos: OCR de Windows sobre la captura
/// preparada (<see cref="LogImage"/>), reconocimiento aproximado contra data/events.json y detección de mensajes
/// nuevos (<see cref="EventLogTracker"/>).
/// </summary>
public sealed class EventLogReader(OcrReader ocr, EventCatalog catalog)
{
    private readonly EventMatcher _matcher = new(catalog);
    private readonly EventLogTracker _tracker = new();

    /// <summary>Mensajes reconocidos que se ven ahora (estabilizados).</summary>
    public IReadOnlyList<string> Visible => _tracker.Visible;

    public async Task<IReadOnlyList<string>> ReadAsync(CapturedImage image)
    {
        var lines = await ReadLinesAsync(ocr, image);
        return Process(lines.Select(l => l.Text).ToList());
    }

    /// <summary>Renglones del log de arriba hacia abajo, con la captura preparada por <see cref="LogImage"/>.</summary>
    public static Task<IReadOnlyList<OcrTextLine>> ReadLinesAsync(OcrReader ocr, CapturedImage image) =>
        ocr.ReadPreparedLinesAsync(LogImage.Prepare(image), LogImage.Scale, LogImage.Margin);

    /// <summary>Procesa renglones ya leídos (de arriba hacia abajo). Sirve también para transcripciones guardadas.</summary>
    public IReadOnlyList<string> Process(IReadOnlyList<string> lines) => Process(_matcher, _tracker, lines);

    public static IReadOnlyList<string> Process(EventMatcher matcher, EventLogTracker tracker, IReadOnlyList<string> lines)
    {
        var (ids, unrecognized) = matcher.MatchAll(lines);
        return tracker.Update(ids, unrecognized > 0);
    }
}
