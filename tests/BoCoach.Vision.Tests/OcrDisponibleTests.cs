using Windows.Media.Ocr;

namespace BoCoach.Vision.Tests;

// Confirma que el target de Windows quedó bien configurado y que las APIs de WinRT se pueden usar.
public class OcrDisponibleTests
{
    [Fact]
    public void La_API_de_OCR_de_Windows_es_accesible()
    {
        Assert.True(OcrEngine.MaxImageDimension > 0);
    }
}
