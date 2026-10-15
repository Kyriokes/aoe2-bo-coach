using System.Runtime.InteropServices;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using Windows.Media.Ocr;

namespace BoCoach.Vision.Tests;

// Confirma que el target de Windows quedó bien configurado y que el OCR de Windows lee texto.
public class OcrDisponibleTests
{
    [Fact]
    public void La_API_de_OCR_de_Windows_es_accesible()
    {
        Assert.True(OcrEngine.MaxImageDimension > 0);
    }

    [Fact]
    public async Task Lee_numeros_claros_sobre_fondo_oscuro()
    {
        var reader = OcrReader.TryCreate();
        if (reader is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)

        var image = TextImage.Render("1234", width: 90, height: 24, fontHeight: 18);

        var text = await reader.ReadAsync(image);

        Assert.Equal("1234", text.Replace(" ", ""));
    }

    [Fact]
    public void Captura_de_pantalla_devuelve_el_tamano_pedido()
    {
        var image = new ScreenCapture().Capture(new PixelRect(0, 0, 8, 5));

        Assert.Equal((8, 5), (image.Width, image.Height));
        Assert.All(Enumerable.Range(0, 40), p => Assert.Equal(255, image.Bgra[p * 4 + 3]));
    }
}

/// <summary>Dibuja texto blanco sobre negro con GDI, parecido a los números del HUD.</summary>
internal static class TextImage
{
    public static CapturedImage Render(string text, int width, int height, int fontHeight)
    {
        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var font = CreateFont(-fontHeight, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 4, 0, "Arial");
        try
        {
            SelectObject(dc, bitmap);
            SelectObject(dc, font);
            SetBkColor(dc, 0x000000);
            SetTextColor(dc, 0xFFFFFF);
            var rect = new Rect { Right = width, Bottom = height };
            ExtTextOut(dc, 4, (height - fontHeight) / 2, 2 /* ETO_OPAQUE */, ref rect, text, (uint)text.Length, IntPtr.Zero);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var pixels = new byte[width * height * 4];
            GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0);
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new CapturedImage(width, height, pixels);
        }
        finally
        {
            DeleteObject(font);
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Colors;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ExtTextOut(IntPtr dc, int x, int y, uint options, ref Rect rect, string text, uint count, IntPtr dx);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
        uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
