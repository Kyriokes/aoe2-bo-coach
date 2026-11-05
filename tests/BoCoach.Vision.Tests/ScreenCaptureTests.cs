using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Tests;

public class ScreenCaptureTests
{
    // GDI no puede armar un bitmap de 4 GB. Antes los retornos no se miraban y el fallo se veía como una captura en negro:
    // "sin reloj", y a los 30 s el coach daba la partida por terminada. BitBlt y GetDIBits no se pueden hacer fallar acá.
    [Fact]
    public void Si_GDI_falla_lo_dice_en_vez_de_devolver_una_captura_en_negro()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ScreenCapture().Capture(new PixelRect(0, 0, 32768, 32768)));

        Assert.Contains("GDI", error.Message);
    }

    [Fact]
    public void Un_rectangulo_vacio_sigue_siendo_un_error_de_quien_llama()
    {
        Assert.Throws<ArgumentException>(() => new ScreenCapture().Capture(new PixelRect(0, 0, 0, 10)));
    }
}
