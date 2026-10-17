using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Hud;

/// <summary>
/// El HUD del mod BoCoach HUD (tools/mod): cada recurso, la población y los inactivos tienen un cuadrado negro puro
/// con la cantidad de aldeanos abajo, y el stock a la derecha; el reloj tiene un rectángulo negro detrás. El negro
/// del mod es exacto, y las texturas del panel del juego nunca lo son, así que alcanza con buscarlo.
/// </summary>
public static class ModPanel
{
    /// <summary>Canal más alto de un píxel para contar como el negro del mod.</summary>
    private const int Black = 8;

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

        CapturedImage Block((int Start, int Length) b) => image.Crop(new PixelRect(b.Start, 0, b.Length, h));
        return (Block(blocks[0]), blocks.Count > 1 ? Block(blocks[1]) : null);
    }

    /// <summary>Si la región tiene el fondo negro del mod (el reloj): más de un tercio de los píxeles negro puro.</summary>
    public static bool HasBlackBackground(CapturedImage image)
    {
        var black = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (IsBlack(image, x, y))
                    black++;
        return black * 3 > image.Width * image.Height;
    }

    private static bool IsSquareColumn(CapturedImage image, int x)
    {
        var black = 0;
        for (var y = 0; y < image.Height; y++)
            if (IsBlack(image, x, y))
                black++;
        return black >= image.Height * SquareColumn;
    }

    private static bool IsBlack(CapturedImage image, int x, int y)
    {
        var (r, g, b) = image.Pixel(x, y);
        return r <= Black && g <= Black && b <= Black;
    }
}
