using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Digits;

/// <summary>Un carácter recortado de una imagen: sus columnas y la grilla normalizada para comparar.</summary>
public sealed record Glyph(int Left, int Right, float[] Cell, float WidthRatio)
{
    public int Width => Right - Left + 1;
}

/// <summary>
/// Separa los caracteres de una región: columnas con tinta seguidas forman un carácter. Un bloque de dos dígitos
/// típicos o más son dígitos pegados (el "00" del reloj) y se parte en partes iguales; uno algo más ancho que un dígito
/// con una columna de poca tinta adentro es un dígito con algo fino pegado (el "0" y el ":" del reloj del HUD propio)
/// y se parte en ese valle. Si además se pasa cómo se parece una grilla a la letra, un bloque así que no se parece a
/// ningún carácter se parte donde las dos partes sí se parecen (el "/2" de "12/20").
/// </summary>
public static class Glyphs
{
    /// <summary>Tinta mínima para que una columna cuente como parte de un carácter.</summary>
    private const float ColumnInk = 0.6f;

    /// <param name="distance">Distancia de una grilla a su carácter más parecido de la letra, o null para no usarla.</param>
    /// <param name="maxDistance">Hasta esta distancia una grilla se parece a un carácter.</param>
    public static IReadOnlyList<Glyph> Extract(CapturedImage image, InkSpec ink, double typicalWidthRatio, Func<float[], float>? distance = null, float maxDistance = float.MaxValue)
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
        var solid = new int[w];
        var runs = new int[w];
        for (var x = 0; x < w; x++)
        {
            var inRun = false;
            for (var y = 0; y < h; y++)
            {
                columns[x] += map[y * w + x];
                var firm = map[y * w + x] >= SolidInk;
                if (firm)
                    solid[x]++;
                if (firm && !inRun)
                    runs[x]++;
                inRun = firm;
            }
        }

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
        Glyph Cut(int left, int right) => Normalize(map, w, h, left, right, textHeight);

        // Un bloque más ancho que un dígito que quedó entero (sin valle) y no se parece a nada: dos caracteres que se
        // tocan con trazo firme (el "/" y el "2" de "12/20"). Se prueba cada corte y gana el que deja las dos partes
        // más parecidas a la letra, si las dos se parecen.
        IEnumerable<Glyph> Recognized(int a, int b)
        {
            var whole = Cut(a, b);
            if (distance is null || b - a + 1 < typical * ValleyMinWidth || distance(whole.Cell) <= maxDistance)
                return [whole];
            (Glyph Left, Glyph Right, float Worst)? best = null;
            for (var x = a + 1; x < b - 1; x++)
            {
                var (left, right) = (Cut(a, x), Cut(x + 1, b));
                var worst = Math.Max(distance(left.Cell), distance(right.Cell));
                if (worst <= maxDistance && (best is null || worst < best.Value.Worst))
                    best = (left, right, worst);
            }
            return best is { } found ? [found.Left, found.Right] : [whole];
        }

        return [.. blocks.SelectMany(block => Split(columns, solid, runs, block.A, block.B, typical)).SelectMany(part => Recognized(part.A, part.B))];
    }

    /// <summary>Un píxel con esta tinta o más es trazo firme; menos, borde suavizado.</summary>
    private const float SolidInk = 0.5f;

    /// <summary>Un bloque de al menos este ancho (en dígitos típicos) puede ser dos caracteres que se tocan.</summary>
    private const double ValleyMinWidth = 1.2;

    /// <summary>Desde este ancho (en dígitos típicos) un bloque sin valle son dígitos pegados: partes iguales.</summary>
    private const double GluedMinWidth = 1.6;

    /// <summary>Columnas desde cada borde del bloque donde no se busca el valle (cada parte queda de 2 o más).</summary>
    private const int ValleyMargin = 2;

    /// <summary>El valle tiene que tener menos de esta parte de la tinta de la columna más cargada del bloque.</summary>
    private const double ValleyDepth = 0.3;

    /// <summary>
    /// Píxeles firmes que puede tener la columna del valle, todos seguidos: donde dos caracteres se tocan hay borde
    /// suavizado y a lo sumo la punta de uno (el "/" de "N/M" toca al dígito que sigue por arriba).
    /// </summary>
    private const int ValleySolidMax = 3;

    /// <summary>
    /// Parte un bloque de columnas con tinta en caracteres. Un bloque más ancho que un dígito con un valle adentro son
    /// dos caracteres que se tocan (el "0" y el ":" del reloj del HUD propio, "4/10" en "N/M", "60" en un stock;
    /// grabación 20261025-024356) y se parte en el valle, que no va a ninguno de los dos (es el borde suavizado de
    /// ambos); se repite en cada parte. Sin valle, desde 1,6 dígitos son dígitos pegados y se parte en partes iguales.
    /// </summary>
    private static IEnumerable<(int A, int B)> Split(float[] columns, int[] solid, int[] runs, int a, int b, double typical)
    {
        var width = b - a + 1;
        if (width >= typical * ValleyMinWidth && Valley(columns, solid, runs, a, b) is { } valley)
            return Split(columns, solid, runs, a, valley - 1, typical).Concat(Split(columns, solid, runs, valley + 1, b, typical));
        if (width < typical * GluedMinWidth)
            return [(a, b)];
        var parts = Math.Max(2, (int)Math.Round(width / typical));
        return Enumerable.Range(0, parts).Select(p => (a + (int)Math.Round(p * width / (double)parts), a + (int)Math.Round((p + 1) * width / (double)parts) - 1));
    }

    /// <summary>
    /// La columna interior con menos tinta entre las que son casi todo borde suavizado (pocos píxeles firmes y
    /// seguidos), si tiene bastante menos que la columna más cargada; si no, null. El hueco de un "0" también tiene
    /// poca tinta, pero firme y en dos tramos (los arcos de arriba y de abajo): no es un valle, y tomándolo "60" se
    /// partía en "6", "1", "1".
    /// </summary>
    private static int? Valley(float[] columns, int[] solid, int[] runs, int a, int b)
    {
        var peak = 0f;
        for (var x = a; x <= b; x++)
            peak = Math.Max(peak, columns[x]);
        var (valley, valleyInk) = (-1, float.MaxValue);
        for (var x = a + ValleyMargin; x <= b - ValleyMargin; x++)
        {
            if (solid[x] <= ValleySolidMax && runs[x] <= 1 && columns[x] < valleyInk)
                (valley, valleyInk) = (x, columns[x]);
        }
        return valley >= 0 && valleyInk <= ValleyDepth * peak ? valley : null;
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
