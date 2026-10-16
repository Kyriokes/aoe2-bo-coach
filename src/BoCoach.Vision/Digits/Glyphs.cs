using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Digits;

/// <summary>Un carácter recortado de una imagen: sus columnas y la grilla normalizada para comparar.</summary>
public sealed record Glyph(int Left, int Right, float[] Cell, float WidthRatio)
{
    public int Width => Right - Left + 1;
}

/// <summary>
/// Separa los caracteres de una región: columnas con tinta seguidas forman un carácter; si un bloque es mucho
/// más ancho que un dígito típico (dígitos pegados, como el "00" del reloj), se parte en partes iguales.
/// </summary>
public static class Glyphs
{
    /// <summary>Tinta mínima para que una columna cuente como parte de un carácter.</summary>
    private const float ColumnInk = 0.6f;

    public static IReadOnlyList<Glyph> Extract(CapturedImage image, InkSpec ink, double typicalWidthRatio)
    {
        var map = ink.InkMap(image);
        var (w, h) = (image.Width, image.Height);

        // Banda de texto: el bloque de filas seguidas con más tinta. Lo que quede afuera (separado por filas
        // vacías, como el borde de la caja en algunos diseños de HUD) se borra para que no se pegue a los dígitos.
        var rowInk = Enumerable.Range(0, h)
            .Select(y => Enumerable.Range(0, w).Sum(x => map[y * w + x] >= 0.5f ? map[y * w + x] : 0))
            .ToArray();
        var band = (Top: -1, Bottom: -1, Ink: 0f);
        for (var y = 0; y < h; y++)
        {
            if (rowInk[y] <= 0)
                continue;
            var top = y;
            var total = 0f;
            while (y < h && rowInk[y] > 0)
                total += rowInk[y++];
            if (total > band.Ink)
                band = (top, y - 1, total);
        }
        if (band.Top < 0)
            return [];
        for (var y = 0; y < h; y++)
        {
            if (y < band.Top || y > band.Bottom)
                Array.Clear(map, y * w, w);
        }
        var textTop = band.Top;
        var textHeight = band.Bottom - band.Top + 1;

        var columns = new float[w];
        for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
                columns[x] += map[y * w + x];

        var blocks = new List<(int A, int B)>();
        for (var x = 0; x < w; x++)
        {
            if (columns[x] < ColumnInk)
                continue;
            var start = x;
            while (x < w && columns[x] >= ColumnInk)
                x++;
            blocks.Add((start, x - 1));
        }

        var typical = Math.Max(2, typicalWidthRatio * textHeight);
        var glyphs = new List<Glyph>();
        foreach (var (a, b) in blocks)
        {
            var width = b - a + 1;
            var parts = Math.Max(1, (int)Math.Round(width / typical));
            if (width < typical * 1.6)
                parts = 1;
            for (var p = 0; p < parts; p++)
            {
                var left = a + (int)Math.Round(p * width / (double)parts);
                var right = a + (int)Math.Round((p + 1) * width / (double)parts) - 1;
                glyphs.Add(Normalize(map, w, h, left, right, textHeight));
            }
        }
        return glyphs;
    }

    /// <summary>
    /// Recorta el carácter a su caja de tinta y lo lleva a una grilla de alto fijo, manteniendo la proporción
    /// (centrado a lo ancho), así un "1" angosto no se confunde con un "8" estirado.
    /// </summary>
    private static Glyph Normalize(float[] map, int w, int h, int left, int right, int textHeight)
    {
        int top = h, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (map[y * w + x] < 0.5f)
                    continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (bottom < 0)
            (top, bottom) = (0, h - 1);

        var glyphHeight = bottom - top + 1;
        var glyphWidth = right - left + 1;
        var scale = (GlyphFont.CellHeight - 1) / (double)Math.Max(glyphHeight, textHeight * 0.6);
        var scaledWidth = Math.Min(GlyphFont.CellWidth, glyphWidth * scale);
        var offsetX = (GlyphFont.CellWidth - scaledWidth) / 2;

        var cell = new float[GlyphFont.CellWidth * GlyphFont.CellHeight];
        for (var cy = 0; cy < GlyphFont.CellHeight; cy++)
        {
            for (var cx = 0; cx < GlyphFont.CellWidth; cx++)
            {
                var sx = left + (cx + 0.5 - offsetX) / scale - 0.5;
                var sy = top + (cy + 0.5) / scale - 0.5;
                if (sx < left - 0.5 || sx > right + 0.5 || sy < top - 0.5 || sy > bottom + 0.5)
                    continue;
                cell[cy * GlyphFont.CellWidth + cx] = Sample(map, w, h, sx, sy);
            }
        }
        return new Glyph(left, right, cell, glyphWidth / (float)textHeight);
    }

    private static float Sample(float[] map, int w, int h, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);
        float At(int xx, int yy) => xx < 0 || yy < 0 || xx >= w || yy >= h ? 0 : map[yy * w + xx];
        return At(x0, y0) * (1 - fx) * (1 - fy) + At(x0 + 1, y0) * fx * (1 - fy)
             + At(x0, y0 + 1) * (1 - fx) * fy + At(x0 + 1, y0 + 1) * fx * fy;
    }

    /// <summary>Distancia entre dos grillas (suma de diferencias al cuadrado).</summary>
    public static float Distance(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }
}
