using System.Runtime.InteropServices.WindowsRuntime;
using BoCoach.Vision.Capture;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace BoCoach.Vision.Ocr;

/// <summary>Un renglón leído: el texto y su posición vertical en la imagen (en píxeles de la captura).</summary>
public sealed record OcrTextLine(string Text, double Top, double Height);

/// <summary>
/// OCR de Windows (Windows.Media.Ocr), sin instalar nada. Antes de leer, la imagen se escala ×3 y se pasa
/// a gris invertido: el HUD tiene texto claro sobre fondo oscuro y el OCR anda mejor al revés.
/// </summary>
public sealed class OcrReader
{
    private readonly OcrEngine _engine;

    private OcrReader(OcrEngine engine)
    {
        _engine = engine;
    }

    public string Language => _engine.RecognizerLanguage.LanguageTag;

    /// <summary>Crea el lector para el idioma pedido (el juego está en inglés), o null si Windows no lo tiene instalado.</summary>
    public static OcrReader? TryCreate(string language = "en-US")
    {
        var lang = new Language(language);
        if (!OcrEngine.IsLanguageSupported(lang))
            return null;
        var engine = OcrEngine.TryCreateFromLanguage(lang);
        return engine is null ? null : new OcrReader(engine);
    }

    /// <summary>Texto leído, una línea por renglón detectado (de arriba hacia abajo).</summary>
    public async Task<string> ReadAsync(CapturedImage image, int scale = 3) =>
        string.Join("\n", (await ReadLinesAsync(image, scale)).Select(l => l.Text));

    /// <summary>
    /// Renglones con su altura en la imagen original, ordenados de arriba hacia abajo. El OCR a veces parte un
    /// renglón en pedazos o los devuelve desordenados: los pedazos a la misma altura se juntan de izquierda a derecha.
    /// </summary>
    public async Task<IReadOnlyList<OcrTextLine>> ReadLinesAsync(CapturedImage image, int scale = 3)
    {
        var prepared = image.InvertedGray().Scale(scale);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            prepared.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, prepared.Width, prepared.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);

        var pieces = result.Lines
            .Where(l => l.Words.Count > 0)
            .Select(l =>
            {
                var top = l.Words.Min(w => w.BoundingRect.Y) / scale;
                var bottom = l.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height) / scale;
                var left = l.Words.Min(w => w.BoundingRect.X) / scale;
                return (Text: l.Text, Left: left, Top: top, Bottom: bottom);
            })
            .OrderBy(p => (p.Top + p.Bottom) / 2)
            .ToList();

        var lines = new List<(List<(string Text, double Left)> Parts, double Top, double Bottom)>();
        foreach (var piece in pieces)
        {
            var middle = (piece.Top + piece.Bottom) / 2;
            var same = lines.FindIndex(l => middle >= l.Top && middle <= l.Bottom);
            if (same >= 0)
            {
                lines[same].Parts.Add((piece.Text, piece.Left));
                lines[same] = (lines[same].Parts, Math.Min(lines[same].Top, piece.Top), Math.Max(lines[same].Bottom, piece.Bottom));
            }
            else
            {
                lines.Add(([(piece.Text, piece.Left)], piece.Top, piece.Bottom));
            }
        }

        return lines
            .Select(l => new OcrTextLine(string.Join(" ", l.Parts.OrderBy(p => p.Left).Select(p => p.Text)), l.Top, l.Bottom - l.Top))
            .ToList();
    }
}
