namespace BoCoach.Vision.Capture;

/// <summary>Rectángulo en píxeles físicos de la pantalla.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Imagen capturada en BGRA de 8 bits por canal, filas de arriba hacia abajo.</summary>
public sealed class CapturedImage
{
    public CapturedImage(int width, int height, byte[] bgra)
    {
        if (bgra.Length != width * height * 4)
            throw new ArgumentException($"Se esperaban {width * height * 4} bytes y hay {bgra.Length}.", nameof(bgra));
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public (byte R, byte G, byte B) Pixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Bgra[i + 2], Bgra[i + 1], Bgra[i]);
    }

    /// <summary>Escala por un factor entero (vecino más cercano): agranda los dígitos chicos del HUD para el OCR.</summary>
    public CapturedImage Scale(int factor)
    {
        if (factor <= 1)
            return this;
        var w = Width * factor;
        var h = Height * factor;
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sourceRow = y / factor * Width;
            for (var x = 0; x < w; x++)
                Buffer.BlockCopy(Bgra, (sourceRow + x / factor) * 4, output, (y * w + x) * 4, 4);
        }
        return new CapturedImage(w, h, output);
    }

    /// <summary>Escala de grises invertida (texto claro del HUD → texto oscuro sobre fondo claro), en BGRA.</summary>
    public CapturedImage InvertedGray()
    {
        var output = new byte[Bgra.Length];
        for (var i = 0; i < Bgra.Length; i += 4)
        {
            var gray = (byte)(255 - (Bgra[i + 2] * 299 + Bgra[i + 1] * 587 + Bgra[i] * 114) / 1000);
            output[i] = output[i + 1] = output[i + 2] = gray;
            output[i + 3] = 255;
        }
        return new CapturedImage(Width, Height, output);
    }
}
