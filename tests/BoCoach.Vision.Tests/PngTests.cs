using BoCoach.Vision.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BoCoach.Vision.Tests;

public class PngTests
{
    internal static CapturedImage Gradient(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bgra[i] = (byte)(x * 7);
                bgra[i + 1] = (byte)(y * 13);
                bgra[i + 2] = (byte)(x * y);
                bgra[i + 3] = 255;
            }
        }
        return new CapturedImage(width, height, bgra);
    }

    [Fact]
    public void Ida_y_vuelta_conserva_los_pixeles()
    {
        var image = Gradient(37, 11);
        using var stream = new MemoryStream();

        Png.Write(image, stream);
        stream.Position = 0;
        var read = Png.Read(stream);

        Assert.Equal(image.Width, read.Width);
        Assert.Equal(image.Height, read.Height);
        Assert.Equal(image.Bgra, read.Bgra);
    }

    [Fact]
    public async Task Windows_lee_los_PNG_que_escribimos()
    {
        var image = Gradient(20, 9);
        using var stream = new MemoryStream();
        Png.Write(image, stream);

        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

        Assert.Equal(image.Bgra, pixels.DetachPixelData());
    }

    [Fact]
    public async Task Lee_PNG_de_Windows_con_cualquier_filtro()
    {
        var image = Gradient(64, 32);
        var random = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, random);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 64, 32, 96, 96, image.Bgra);
        await encoder.FlushAsync();
        random.Seek(0);

        var read = Png.Read(random.AsStreamForRead());

        Assert.Equal(image.Bgra, read.Bgra);
    }

    [Fact]
    public void Rechaza_lo_que_no_es_PNG()
    {
        using var stream = new MemoryStream("no soy un png"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => Png.Read(stream));
    }
}
