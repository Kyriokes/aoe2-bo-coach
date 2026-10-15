using System.Runtime.InteropServices.WindowsRuntime;
using BoCoach.Vision.Capture;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace BoCoach.Vision.Ocr;

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

    /// <summary>Texto leído, una línea por renglón detectado.</summary>
    public async Task<string> ReadAsync(CapturedImage image, int scale = 3)
    {
        var prepared = image.InvertedGray().Scale(scale);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            prepared.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, prepared.Width, prepared.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }
}
