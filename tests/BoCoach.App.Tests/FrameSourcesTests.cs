using BoCoach.App.Coaching;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Tests;

public class FrameSourcesTests
{
    private sealed class FakeScreen : IScreenSource
    {
        public PixelRect CaptureArea => new(0, 0, 1920, 1080);
        public bool GameFound => true;

        public CapturedImage Capture(PixelRect rect) =>
            new(rect.Width, rect.Height, new byte[rect.Width * rect.Height * 4]);
    }

    // B4 (b): con el HUD a otra escala, cada región llega a los lectores en los píxeles de 1080p de las letras.
    [Fact]
    public void Con_el_HUD_a_otra_escala_las_capturas_llegan_reescaladas()
    {
        var layout = RegionLayout.Default.WithHudScale(1.5);
        var food = layout.PixelsOf(RegionNames.Food, 1920, 1080);

        var frame = new ScreenFrames(new FakeScreen(), layout).Next()!;
        var image = frame.Region(RegionNames.Food)!;

        Assert.Equal(((int)Math.Round(food.Width / 1.5), (int)Math.Round(food.Height / 1.5)), (image.Width, image.Height));
    }

    [Fact]
    public void A_escala_1_las_capturas_llegan_tal_cual()
    {
        var food = RegionLayout.Default.PixelsOf(RegionNames.Food, 1920, 1080);

        var image = new ScreenFrames(new FakeScreen(), RegionLayout.Default).Next()!.Region(RegionNames.Food)!;

        Assert.Equal((food.Width, food.Height), (image.Width, image.Height));
    }
}
