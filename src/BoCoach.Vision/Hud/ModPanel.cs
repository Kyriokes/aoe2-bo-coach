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
    private const int Black = 2;

    /// <summary>Parte de la columna en negro para que sea del cuadrado (los trazos del número tapan el resto).</summary>
    private const double SquareColumn = 0.1;

    /// <summary>
    /// Separa una caja en el cuadrado de los aldeanos (el primer bloque negro) y el fondo negro del stock (el
    /// siguiente), o null si la caja no tiene el cuadrado (juego sin el mod). El stock es null si la caja no lo tiene
    /// (inactivos).
    /// </summary>
    public static (CapturedImage Square, CapturedImage? Stock)? Split(CapturedImage image)
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
        if (blocks.Count == 0)
            return null;

        // Otros paneles (KoBHV) también tienen fondos negro puro; lo único del mod es la franja de un solo color
        // justo arriba del negro del cuadrado.
        var square = Body(image, blocks[0]);
        if (!HasBandAbove(image, square))
            return null;
        return (Crop(image, square), blocks.Count > 1 ? Crop(image, Body(image, blocks[1])) : null);
    }

    /// <summary>
    /// El cuerpo negro del bloque. A lo ancho, de la primera a la última columna firme (con casi tanto negro como la
    /// que más tiene): los bordes del bloque pueden ser columnas suavizadas con poco negro. A lo alto, sin lo que no
    /// es negro arriba y abajo (la franja de color del cuadrado y el panel que la caja toma de más): las filas
    /// seguidas (las más) donde los dos bordes son negros.
    /// </summary>
    private static (int Left, int Right, int Top, int Height) Body(CapturedImage image, (int Start, int Length) block)
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
    private static bool HasBandAbove(CapturedImage image, (int Left, int Right, int Top, int Height) body)
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

    private static bool IsSquareColumn(CapturedImage image, int x) => BlackInColumn(image, x) >= image.Height * SquareColumn;

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
