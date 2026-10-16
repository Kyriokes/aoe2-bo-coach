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
    private CapturedImage? _lastMask;
    private IReadOnlyList<string> _lastLines = [];

    /// <summary>Mensajes reconocidos que se ven ahora (estabilizados).</summary>
    public IReadOnlyList<string> Visible => _tracker.Visible;

    /// <summary>Cuántas veces se pasó el OCR (para medir cuánto se ahorra).</summary>
    public int OcrCalls { get; private set; }

    /// <summary>
    /// Lee una captura y devuelve los eventos nuevos. El OCR solo se pasa si hay texto y si cambió desde la captura
    /// anterior: el texto del log suele quedarse quieto varios segundos mientras el mapa de atrás se mueve.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadAsync(CapturedImage image)
    {
        var mask = LogImage.Mask(image);
        IReadOnlyList<string> lines;
        if (!LogImage.HasInk(mask))
        {
            lines = [];
        }
        else if (_lastMask is not null && LogImage.SameText(mask, _lastMask))
        {
            lines = _lastLines;
        }
        else
        {
            OcrCalls++;
            lines = (await ocr.ReadPreparedLinesAsync(LogImage.PrepareMask(mask), LogImage.Scale, LogImage.Margin)).Select(l => l.Text).ToList();
        }
        (_lastMask, _lastLines) = (mask, lines);
        return Process(lines);
    }

    /// <summary>
    /// Renglones del log de arriba hacia abajo, con la captura preparada por <see cref="LogImage"/>. Si no hay texto
    /// blanco no se pasa el OCR.
    /// </summary>
    public static async Task<IReadOnlyList<OcrTextLine>> ReadLinesAsync(OcrReader ocr, CapturedImage image) =>
        LogImage.HasText(image)
            ? await ocr.ReadPreparedLinesAsync(LogImage.Prepare(image), LogImage.Scale, LogImage.Margin)
            : [];

    /// <summary>Procesa renglones ya leídos (de arriba hacia abajo). Sirve también para transcripciones guardadas.</summary>
    public IReadOnlyList<string> Process(IReadOnlyList<string> lines) => Process(_matcher, _tracker, lines);

    public static IReadOnlyList<string> Process(EventMatcher matcher, EventLogTracker tracker, IReadOnlyList<string> lines)
    {
        var (ids, unrecognized) = matcher.MatchAll(lines);
        return tracker.Update(ids, unrecognized > 0);
    }
}
