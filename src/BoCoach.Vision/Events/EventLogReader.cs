using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Events;

/// <summary>
/// Lee el log de eventos captura por captura y devuelve solo los eventos nuevos: OCR de Windows sobre la captura
/// preparada (<see cref="LogImage"/>), reconocimiento aproximado contra data/events.json y detección de mensajes
/// nuevos (<see cref="EventLogTracker"/>).
/// </summary>
/// <param name="ocr">null para reproducir renglones ya leídos (<see cref="Process(IReadOnlyList{string})"/>): sin OCR no se leen imágenes.</param>
/// <param name="captureIntervalMs">Cada cuánto llegan las capturas: el log cuenta capturas para saber cuándo quedó vacío.</param>
public sealed class EventLogReader(OcrReader? ocr, EventCatalog catalog, double captureIntervalMs = 500)
{
    private readonly EventMatcher _matcher = new(catalog);
    private readonly EventLogTracker _tracker = new() { EmptyFrames = EventLogTracker.EmptyFramesFor(captureIntervalMs) };
    private CapturedImage? _lastMask;
    private IReadOnlyList<string> _lastLines = [];

    /// <summary>Mensajes reconocidos que se ven ahora (estabilizados).</summary>
    public IReadOnlyList<string> Visible => _tracker.Visible;

    /// <summary>Lo que va al registro de partida por la última captura: la frase del catálogo de cada mensaje, nunca lo leído (ver <see cref="EventMatcher.Scrub"/>).</summary>
    public IReadOnlyList<string> LastLines { get; private set; } = [];

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
            if (ocr is null)
                throw new InvalidOperationException("Sin OCR no se leen imágenes del log.");
            OcrCalls++;
            lines = (await ocr.ReadPreparedLinesAsync(LogImage.PrepareMask(mask), LogImage.Scale, LogImage.Margin)).Select(l => l.Text).ToList();
        }
        // Solo si cambiaron: reconocer cuesta, y el texto suele quedarse quieto varios segundos.
        if (!ReferenceEquals(lines, _lastLines))
            LastLines = _matcher.Scrub(lines);
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
