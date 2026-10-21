namespace BoCoach.Vision.Capture;

/// <summary>De dónde salen las imágenes: la pantalla real o, en tests y replays, otra fuente.</summary>
public interface IScreenSource
{
    /// <summary>Zona de la pantalla donde está el juego, en píxeles físicos. Las regiones son relativas a ella.</summary>
    PixelRect CaptureArea { get; }

    /// <summary>True si <see cref="CaptureArea"/> es la ventana del juego (y no la pantalla principal por descarte).</summary>
    bool GameFound { get; }

    /// <summary>
    /// False si el juego está abierto pero minimizado o detrás de otra ventana (alt-tab): no vale la pena capturar.
    /// True si está al frente, o si no está abierto (se captura la pantalla principal, como siempre).
    /// </summary>
    bool GameInFront => true;

    CapturedImage Capture(PixelRect rect);
}

/// <summary>
/// Captura de pantalla con GDI (BitBlt), solo del rectángulo pedido. Sigue a la ventana del juego, así anda
/// en cualquier monitor y en modo ventana; si el juego no está abierto, usa la pantalla principal.
/// Aunque no se pide CAPTUREBLT, las ventanas transparentes (overlay y calibración) salen en la captura: en las
/// grabaciones se ve la GD dentro de la franja del mod, y por eso el lector mide la franja por el costado.
/// El proceso tiene que ser "DPI aware" para que las coordenadas sean píxeles físicos.
/// </summary>
public sealed class ScreenCapture : IScreenSource
{
    public PixelRect CaptureArea => GameWindow.FindClientArea() ?? GameWindow.PrimaryScreen;

    public bool GameFound => GameWindow.FindClientArea() is not null;

    public bool GameInFront => GameWindow.IsInFront() ?? true;

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
