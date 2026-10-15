namespace BoCoach.Vision.Capture;

/// <summary>De dónde salen las imágenes: la pantalla real o, en tests y replays, otra fuente.</summary>
public interface IScreenSource
{
    /// <summary>Tamaño de la pantalla en píxeles físicos.</summary>
    (int Width, int Height) ScreenSize { get; }

    CapturedImage Capture(PixelRect rect);
}

/// <summary>
/// Captura de la pantalla principal con GDI (BitBlt), solo del rectángulo pedido.
/// Sin CAPTUREBLT, las ventanas transparentes (overlay y calibración) no salen en la captura.
/// El proceso tiene que ser "DPI aware" para que las coordenadas sean píxeles físicos.
/// </summary>
public sealed class ScreenCapture : IScreenSource
{
    public (int Width, int Height) ScreenSize =>
        (Gdi.GetSystemMetrics(Gdi.SmCxScreen), Gdi.GetSystemMetrics(Gdi.SmCyScreen));

    public CapturedImage Capture(PixelRect rect)
    {
        if (rect.IsEmpty)
            throw new ArgumentException("El rectángulo está vacío.", nameof(rect));

        var screen = Gdi.GetDC(IntPtr.Zero);
        var memory = Gdi.CreateCompatibleDC(screen);
        var bitmap = Gdi.CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        var previous = Gdi.SelectObject(memory, bitmap);
        try
        {
            Gdi.BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.X, rect.Y, Gdi.SrcCopy);
            Gdi.SelectObject(memory, previous);

            var info = new Gdi.BitmapInfo
            {
                Header = new Gdi.BitmapInfoHeader
                {
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Gdi.BitmapInfoHeader>(),
                    Width = rect.Width,
                    Height = -rect.Height, // negativo = filas de arriba hacia abajo
                    Planes = 1,
                    BitCount = 32,
                },
            };
            var pixels = new byte[rect.Width * rect.Height * 4];
            Gdi.GetDIBits(memory, bitmap, 0, (uint)rect.Height, pixels, ref info, 0);
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255; // GDI deja el alfa en 0
            return new CapturedImage(rect.Width, rect.Height, pixels);
        }
        finally
        {
            Gdi.DeleteObject(bitmap);
            Gdi.DeleteDC(memory);
            Gdi.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
