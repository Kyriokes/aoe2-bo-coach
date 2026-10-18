using System.Windows.Media.Imaging;
using BoCoach.App.Overlay;

namespace BoCoach.App.Tests;

public sealed class GameIconsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-icons-");

    public void Dispose() => _dir.Delete(recursive: true);

    /// <summary>Un .DDS como los de widgetui: encabezado de 128 bytes y píxeles R G B A de 32 bits.</summary>
    private string Dds(int width, int height, byte[] pixels, int bits = 32)
    {
        var bytes = new byte[128 + pixels.Length];
        "DDS "u8.CopyTo(bytes);
        BitConverter.GetBytes(124).CopyTo(bytes, 4);
        BitConverter.GetBytes(height).CopyTo(bytes, 12);
        BitConverter.GetBytes(width).CopyTo(bytes, 16);
        BitConverter.GetBytes(32).CopyTo(bytes, 76);
        BitConverter.GetBytes(0x41u).CopyTo(bytes, 80); // RGB + alfa
        BitConverter.GetBytes(bits).CopyTo(bytes, 88);
        BitConverter.GetBytes(0xFFu).CopyTo(bytes, 92);
        BitConverter.GetBytes(0xFF00u).CopyTo(bytes, 96);
        BitConverter.GetBytes(0xFF0000u).CopyTo(bytes, 100);
        BitConverter.GetBytes(0xFF000000u).CopyTo(bytes, 104);
        pixels.CopyTo(bytes, 128);
        var path = Path.Combine(_dir.FullName, $"{Guid.NewGuid():N}.dds");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Lee_un_DDS_sin_comprimir_de_la_interfaz_del_juego()
    {
        // Un píxel rojo opaco y uno azul semitransparente.
        var image = GameIcons.Dds(Dds(2, 1, [255, 0, 0, 255, 0, 0, 255, 128]));

        Assert.NotNull(image);
        Assert.Equal(2, image.PixelWidth);
        var read = new byte[8];
        new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(read, 8, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 128 }, read);
    }

    [Fact]
    public void Otro_formato_o_un_archivo_que_falta_no_son_un_icono()
    {
        Assert.Null(GameIcons.Dds(Dds(1, 1, [0, 0, 0], bits: 24)));
        Assert.Null(GameIcons.Dds(Path.Combine(_dir.FullName, "no-existe.dds")));
        Assert.Equal(new GameIcons(null, null, null), GameIcons.Load(_dir.FullName));
    }
}
