using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Hud;

/// <summary>
/// El HUD del mod BoCoach HUD (tools/mod): cada recurso, la población y los inactivos tienen un cuadrado negro puro
/// con la cantidad de aldeanos abajo, y el stock a la derecha; el reloj tiene un rectángulo negro detrás. El negro
/// del mod es exacto, y las texturas del panel del juego nunca lo son, así que alcanza con buscarlo.
/// </summary>
public static class ModPanel
{
    /// <summary>
    /// Canal más alto de un píxel para contar como el negro del mod (que se captura 0/0/0). Más alto, la textura
    /// oscura del panel entre el cuadrado de población y su "N/M" (valores de 4 a 7) unía los dos bloques.
    /// </summary>
    internal const int Black = 2;

    /// <summary>Parte de la columna en negro para que sea del cuadrado (los trazos del número tapan el resto).</summary>
    private const double SquareColumn = 0.1;

    /// <summary>
    /// Separa una caja en el cuadrado de los aldeanos (el primer bloque negro) y el fondo negro del stock (el
    /// siguiente), o null si la caja no tiene el cuadrado (juego sin el mod). El stock es null si la caja no lo tiene
    /// (inactivos).
    /// </summary>
    public static (CapturedImage Square, CapturedImage? Stock)? Split(CapturedImage image) =>
        Find(image) is { } found
            ? (Crop(image, found.Square), found.Stock is { } stock ? Crop(image, stock) : null)
            : null;

    /// <summary>
    /// La franja de color del cuadrado (la mitad de arriba, que es decoración del mod), en píxeles de la caja, o null
    /// si la caja no tiene el cuadrado. Ahí dibuja el overlay la GD sin tapar nada del juego. Las últimas filas, justo
    /// arriba del negro, se usan para reconocer el mod: el overlay no las tiene que tocar.
    /// </summary>
    public static PixelRect? Band(CapturedImage image)
    {
        if (Find(image) is not { } found)
            return null;
        var (left, right, bodyTop, _) = found.Square;
        // Se mide por el costado: en vivo la captura incluye nuestro overlay, que dibuja la GD centrada en la franja.
        var x = left + Math.Max(1, (right - left) / 20);
        var reference = image.Pixel(x, bodyTop - 2);
        var top = bodyTop - 2;
        while (top > 0 && Similar(image.Pixel(x, top - 1), reference))
            top--;
        return new PixelRect(left, top, right - left + 1, bodyTop - top);
    }

    internal static bool Similar((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Abs(a.R - b.R) <= 30 && Math.Abs(a.G - b.G) <= 30 && Math.Abs(a.B - b.B) <= 30;

    /// <summary>El cuerpo negro del cuadrado y el del stock (si hay), o null si la caja no es del mod.</summary>
    private static ((int Left, int Right, int Top, int Height) Square, (int Left, int Right, int Top, int Height)? Stock)? Find(CapturedImage image)
    {
        var (w, h) = (image.Width, image.Height);
        var blocks = new List<(int Start, int Length)>();
        for (var x = 0; x < w; x++)
        {
            if (!IsSquareColumn(image, x))
                continue;
            var start = x;
            while (x < w && IsSquareColumn(image, x))
                x++;
            // El cuadrado es casi tan ancho como alto: un tramo angosto es otra cosa (el borde negro de un número).
            if (x - start >= h * 0.5)
                blocks.Add((start, x - start));
        }
        // Otros paneles (KoBHV) también tienen fondos negro puro; lo único del mod es la franja de un solo color
        // justo arriba del negro del cuadrado.
        if (blocks.Count > 0)
        {
            var square = Body(image, blocks[0]);
            if (HasBandAbove(image, square))
                return (square, blocks.Count > 1 ? Body(image, blocks[1]) : null);
        }
        return FromBand(image);
    }

    /// <summary>Canal más alto de lo oscuro cuando el negro no es puro (captura comprimida, panel negro de otra civ).</summary>
    private const int Dark = 60;

    /// <summary>
    /// Cuando el negro no separa el cuadrado (la civ tiene el panel negro y se une con el cuadrado y el stock, o la
    /// captura vino comprimida y el negro dejó de ser puro), el cuadrado se ubica por su franja de color: un tramo
    /// parejo de casi el alto de la caja, con oscuro debajo. El cuadrado tiene el ancho de la franja y es cuadrado
    /// (franja incluida); el stock es lo oscuro a su derecha, si la caja sigue.
    /// </summary>
    private static ((int Left, int Right, int Top, int Height) Square, (int Left, int Right, int Top, int Height)? Stock)? FromBand(CapturedImage image)
    {
        var (w, h) = (image.Width, image.Height);
        var minWidth = Math.Max(6, (int)(h * 0.6));
        for (var y = 0; y < h - 2; y++)
        {
            if (EvenRun(image, y, minWidth) is not { } run)
                continue;
            // Las filas de la franja: el mismo tramo hacia abajo (la GD del overlay solo tapa el medio de las de arriba).
            var bottom = y;
            while (bottom + 1 < h && EvenRun(image, bottom + 1, minWidth) is { } next && Math.Abs(next.Left - run.Left) <= 2 && Math.Abs(next.Right - run.Right) <= 2)
                bottom++;
            var (left, right) = run;
            var top = bottom + 1;
            if (bottom - y < 2 || top >= h || !MostlyDark(image, left + 2, right - 2, Math.Min(h - 1, top + 1)))
            {
                y = bottom;
                continue;
            }
            // Hacia abajo, mientras los dos costados sigan oscuros (con el panel negro, hasta el borde de la caja), nunca
            // más alto que ancho.
            var height = 0;
            while (top + height < h && height <= right - left && IsDark(image.Pixel(left + 1, top + height)) && IsDark(image.Pixel(right - 1, top + height)))
                height++;
            if (height < 3)
                return null;
            var square = (left, right, top, height);
            // El stock empieza en la primera columna oscura después del cuadrado (con el panel de textura, después del
            // pedazo de panel que los separa) y va hasta el final de la caja. Su fondo negro es tan alto como el cuadrado con
            // la franja: los números van centrados en todo ese alto.
            var stockLeft = right + 2;
            while (stockLeft < w && DarkInColumn(image, stockLeft, top, height) < height * 0.6)
                stockLeft++;
            var stockWidth = w - stockLeft;
            return stockWidth >= (right - left + 1) / 2
                ? (square, (stockLeft, w - 1, y, top + height - y))
                : (square, null);
        }
        return null;
    }

    /// <summary>El tramo más largo de la fila con un solo color, no oscuro ni blanco, de al menos <paramref name="minWidth"/>.</summary>
    private static (int Left, int Right)? EvenRun(CapturedImage image, int y, int minWidth)
    {
        (int Left, int Right)? best = null;
        for (var x = 0; x < image.Width;)
        {
            var start = image.Pixel(x, y);
            var max = Math.Max(start.R, Math.Max(start.G, start.B));
            var min = Math.Min(start.R, Math.Min(start.G, start.B));
            if (max < 80 || min >= 200)
            {
                x++;
                continue;
            }
            var end = x + 1;
            while (end < image.Width && Similar(image.Pixel(end, y), start))
                end++;
            if (end - x >= minWidth && (best is null || end - x > best.Value.Right - best.Value.Left + 1))
                best = (x, end - 1);
            x = end;
        }
        return best;
    }

    private static bool MostlyDark(CapturedImage image, int left, int right, int y)
    {
        if (right < left)
            return false;
        var dark = 0;
        for (var x = left; x <= right; x++)
            if (IsDark(image.Pixel(x, y)))
                dark++;
        return dark >= (right - left + 1) * 0.8;
    }

    private static int DarkInColumn(CapturedImage image, int x, int top, int height)
    {
        var dark = 0;
        for (var y = top; y < top + height; y++)
            if (IsDark(image.Pixel(x, y)))
                dark++;
        return dark;
    }

    private static bool IsDark((byte R, byte G, byte B) p) => p.R <= Dark && p.G <= Dark && p.B <= Dark;

    /// <summary>
    /// El cuerpo negro del bloque. A lo ancho, de la primera a la última columna firme (con casi tanto negro como la
    /// que más tiene): los bordes del bloque pueden ser columnas suavizadas con poco negro. A lo alto, sin lo que no
    /// es negro arriba y abajo (la franja de color del cuadrado y el panel que la caja toma de más): las filas
    /// seguidas (las más) donde los dos bordes son negros.
    /// </summary>
    internal static (int Left, int Right, int Top, int Height) Body(CapturedImage image, (int Start, int Length) block)
    {
        var counts = Enumerable.Range(block.Start, block.Length).Select(x => BlackInColumn(image, x)).ToArray();
        var firm = counts.Max() * 0.8;
        var left = block.Start + Array.FindIndex(counts, c => c >= firm);
        var right = block.Start + Array.FindLastIndex(counts, c => c >= firm);
        var (bestTop, bestHeight) = (0, 0);
        for (var y = 0; y < image.Height; y++)
        {
            if (!(IsBlack(image, left, y) && IsBlack(image, right, y)))
                continue;
            var top = y;
            while (y < image.Height && IsBlack(image, left, y) && IsBlack(image, right, y))
                y++;
            if (y - top > bestHeight)
                (bestTop, bestHeight) = (top, y - top);
        }
        return bestHeight == 0 ? (left, right, 0, image.Height) : (left, right, bestTop, bestHeight);
    }

    private static CapturedImage Crop(CapturedImage image, (int Left, int Right, int Top, int Height) body) =>
        image.Crop(new PixelRect(body.Left, body.Top, body.Right - body.Left + 1, body.Height));

    /// <summary>Si dos filas arriba del cuerpo hay un color parejo y no oscuro de borde a borde (la franja del mod).</summary>
    internal static bool HasBandAbove(CapturedImage image, (int Left, int Right, int Top, int Height) body)
    {
        var y = body.Top - 2;
        if (y < 0 || body.Right - body.Left < 6)
            return false;
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        for (var x = body.Left + 2; x <= body.Right - 2; x++)
        {
            var (r, g, b) = image.Pixel(x, y);
            (minR, maxR) = (Math.Min(minR, r), Math.Max(maxR, r));
            (minG, maxG) = (Math.Min(minG, g), Math.Max(maxG, g));
            (minB, maxB) = (Math.Min(minB, b), Math.Max(maxB, b));
        }
        const int even = 30;
        return maxR - minR <= even && maxG - minG <= even && maxB - minB <= even && Math.Max(maxR, Math.Max(maxG, maxB)) >= 80;
    }

    /// <summary>Si la región tiene el fondo negro del mod (el reloj): más de un cuarto de los píxeles negro puro.</summary>
    public static bool HasBlackBackground(CapturedImage image)
    {
        var black = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (IsBlack(image, x, y))
                    black++;
        return black * 4 > image.Width * image.Height;
    }

    internal static bool IsSquareColumn(CapturedImage image, int x) => BlackInColumn(image, x) >= image.Height * SquareColumn;

    private static int BlackInColumn(CapturedImage image, int x)
    {
        var black = 0;
        for (var y = 0; y < image.Height; y++)
            if (IsBlack(image, x, y))
                black++;
        return black;
    }

    private static bool IsBlack(CapturedImage image, int x, int y)
    {
        var (r, g, b) = image.Pixel(x, y);
        return r <= Black && g <= Black && b <= Black;
    }
}
