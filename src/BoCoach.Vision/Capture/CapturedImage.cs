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

    /// <summary>
    /// Escala por un factor entero con interpolación bilineal: los bordes de las letras quedan suaves en vez de
    /// escalonados, que al OCR le sirve más.
    /// </summary>
    public CapturedImage ScaleSmooth(int factor)
    {
        if (factor <= 1)
            return this;
        var w = Width * factor;
        var h = Height * factor;
        var output = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Clamp((y + 0.5) / factor - 0.5, 0, Height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, Height - 1);
            var fy = sy - y0;
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Clamp((x + 0.5) / factor - 0.5, 0, Width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, Width - 1);
                var fx = sx - x0;
                var o = (y * w + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var v = Bgra[(y0 * Width + x0) * 4 + c] * (1 - fx) * (1 - fy)
                          + Bgra[(y0 * Width + x1) * 4 + c] * fx * (1 - fy)
                          + Bgra[(y1 * Width + x0) * 4 + c] * (1 - fx) * fy
                          + Bgra[(y1 * Width + x1) * 4 + c] * fx * fy;
                    output[o + c] = (byte)Math.Round(v);
                }
                output[o + 3] = 255;
            }
        }
        return new CapturedImage(w, h, output);
    }

    /// <summary>Agrega un margen de <paramref name="margin"/> píxeles grises de valor <paramref name="value"/> alrededor.</summary>
    public CapturedImage Padded(int margin, byte value = 255)
    {
        if (margin <= 0)
            return this;
        var w = Width + 2 * margin;
        var h = Height + 2 * margin;
        var output = new byte[w * h * 4];
        for (var i = 0; i < output.Length; i += 4)
            (output[i], output[i + 1], output[i + 2], output[i + 3]) = (value, value, value, 255);
        for (var y = 0; y < Height; y++)
            Buffer.BlockCopy(Bgra, y * Width * 4, output, ((y + margin) * w + margin) * 4, Width * 4);
        return new CapturedImage(w, h, output);
    }

    /// <summary>
    /// Multiplica el brillo para que lo más claro de la imagen (el 0,2 % de los píxeles) llegue a 255, hasta
    /// <paramref name="maxGain"/> veces. Con el juego en pausa todo se ve ×0,2: así el texto vuelve a verse blanco.
    /// Queda igual una imagen que ya tiene blancos, que es toda negra (por el tope) o en la que lo más claro ocupa
    /// buena parte (una zona gris pareja no es texto oscurecido).
    /// </summary>
    public CapturedImage Stretched(double maxGain)
    {
        var histogram = new int[256];
        for (var i = 0; i < Bgra.Length; i += 4)
            histogram[Math.Max(Bgra[i + 2], Math.Max(Bgra[i + 1], Bgra[i]))]++;
        var pixels = Width * Height;
        var brightest = pixels / 500;
        var peak = 255;
        for (var count = 0; peak > 0; peak--)
        {
            count += histogram[peak];
            if (count > brightest)
                break;
        }

        var gain = Math.Min(maxGain, 255.0 / Math.Max(1, peak));
        if (gain <= 1.01 || histogram.Skip(peak * 4 / 5).Sum() > pixels / 4)
            return this;
        var output = new byte[Bgra.Length];
        for (var i = 0; i < Bgra.Length; i += 4)
        {
            output[i] = (byte)Math.Min(255, Bgra[i] * gain);
            output[i + 1] = (byte)Math.Min(255, Bgra[i + 1] * gain);
            output[i + 2] = (byte)Math.Min(255, Bgra[i + 2] * gain);
            output[i + 3] = 255;
        }
        return new CapturedImage(Width, Height, output);
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
