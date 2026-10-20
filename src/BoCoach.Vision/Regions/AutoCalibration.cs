using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Regions;

/// <param name="Layout">Las regiones para la pantalla capturada (resolución de referencia = la de la captura).</param>
/// <param name="Scale">Tamaño del HUD respecto de 1920×1080 (1 = igual; las letras y los íconos están medidos a 1080p).</param>
/// <param name="Squares">Los cuadrados negros con franja del mod (madera, comida, oro, piedra, población, inactivos), de izquierda a derecha.</param>
/// <param name="Clock">El rectángulo negro detrás del reloj.</param>
public sealed record AutoCalibrationResult(RegionLayout Layout, double Scale, IReadOnlyList<PixelRect> Squares, PixelRect Clock);

/// <summary>
/// Calibración sola, con el mod BoCoach HUD: los seis cuadrados negros con franja de color y el rectángulo negro del
/// reloj son anclas que se encuentran en cualquier resolución (el negro del mod es exacto y las texturas del panel
/// nunca lo son; el cuadro de la edad del juego también es negro, pero no tiene franja y está más a la izquierda que
/// el reloj). De las anclas salen las cajas de recursos, población, inactivos, reloj y edad como las dibujaría la
/// calibración manual; la cola de producción se ubica con la escala, y el log y los puntajes quedan donde dice la
/// calibración de referencia (van pegados a los bordes de la pantalla).
/// </summary>
public static class AutoCalibration
{
    /// <summary>Parte de arriba de la pantalla donde está el panel de recursos (a 1080p, 86 px: los cuadrados van de la fila 7 a la 43).</summary>
    private const double TopBand = 0.08;

    /// <summary>Parte de la pantalla, desde la izquierda, donde entran los cuadrados y el reloj (a 1080p el reloj termina en x = 816).</summary>
    private const double LeftPart = 0.5;

    /// <summary>Ancho de un cuadrado a 1920×1080 (medido en la grabación 20261018-132830): con la franja encima, un cuadrado de 36×36.</summary>
    public const double ReferenceSquareWidth = 36;

    /// <summary>Píxeles negros que tiene que tener una columna de la franja de arriba para ser de un bloque del mod (los números blancos tapan el resto).</summary>
    private const int ColumnBlack = 3;

    /// <summary>Parte en negro de un rectángulo liso con números blancos encima (el reloj, la edad): menos es texto con sombra u otra cosa.</summary>
    private const double SolidBlack = 0.5;

    // Márgenes como los de la calibración manual del usuario: 1 a la izquierda, 2 arriba de la franja, 3 debajo del
    // cuerpo, y 4 entre una caja y el cuadrado siguiente.
    private const int LeftMargin = 1;
    private const int TopMargin = 2;
    private const int BottomMargin = 3;
    private const int Gap = 4;

    public static AutoCalibrationResult? Detect(CapturedImage screen, RegionLayout reference)
    {
        var strip = Strip(screen);
        var squares = new List<Body>();
        var rects = new List<Body>();
        foreach (var body in Bodies(strip))
        {
            if (body.Height < 8 || body.Width < 10)
                continue;
            // La franja del cuadrado es alta (la mitad del cuadrado); una fila pareja del panel encima de un fondo de stock no lo es.
            if (ModPanel.HasBandAbove(strip, body.Tuple) && body.Width <= body.Height * 3 && body.Top - BandTop(strip, body) >= body.Width / 4.0)
                squares.Add(body);
            else if (body.Width >= body.Height * 2.5 && BlackFraction(strip, body) >= SolidBlack)
                rects.Add(body);
        }
        if (squares.Count < 6)
            return null;
        squares = [.. squares.OrderBy(s => s.Left).Take(6)];
        var idle = squares[5];
        var scale = squares.Average(s => s.Width) / ReferenceSquareWidth;

        // A la derecha de los inactivos hay dos rectángulos negros con texto blanco: la edad del juego y el reloj del
        // mod. Cada uno es el más cercano a donde la calibración de referencia lo espera, con la escala.
        var referenceClock = reference.PixelsOf(RegionNames.Clock, 1920, 1080);
        var referenceAge = reference.PixelsOf(RegionNames.Age, 1920, 1080);
        var candidates = rects.Where(r => r.Left > idle.Right).ToList();
        if (candidates.Count == 0)
            return null;
        var clock = candidates.MinBy(r => Math.Abs(r.Left - referenceClock.X * scale))!;
        var age = candidates.Where(r => r != clock && r.Right < clock.Left).MinBy(r => Math.Abs(r.Left - referenceAge.X * scale));

        // Los márgenes, como en la calibración manual del usuario a 1080p, van con la escala.
        var (leftMargin, topMargin, bottomMargin, gap) = (Scaled(LeftMargin, scale), Scaled(TopMargin, scale), Scaled(BottomMargin, scale), Scaled(Gap, scale));
        var boxes = new Dictionary<string, PixelRect>();
        string[] names = [RegionNames.Wood, RegionNames.Food, RegionNames.Gold, RegionNames.Stone, RegionNames.Population, RegionNames.IdleVillagers];
        for (var i = 0; i < 6; i++)
        {
            var square = squares[i];
            var top = BandTop(strip, square) - topMargin;
            var left = square.Left - leftMargin;
            var right = i < 5 ? squares[i + 1].Left - gap : square.Right + 1 + leftMargin;
            var bottom = square.Top + square.Height + bottomMargin;
            boxes[names[i]] = new PixelRect(left, top, right - left, bottom - top);
        }
        boxes[RegionNames.Clock] = clock.Rect;
        boxes[RegionNames.Age] = age is { } found
            ? new PixelRect(found.Left - topMargin, found.Top - topMargin, found.Width + 2 * topMargin, found.Height + 2 * topMargin)
            : new PixelRect(clock.Left - Scaled(referenceClock.X - referenceAge.X, scale), Scaled(referenceAge.Y, scale), Scaled(referenceAge.Width, scale), Scaled(referenceAge.Height, scale));
        var referenceQueue = reference.PixelsOf(RegionNames.ProductionQueue, 1920, 1080);
        boxes[RegionNames.ProductionQueue] = new PixelRect(
            Scaled(referenceQueue.X, scale), Scaled(referenceQueue.Y, scale), Scaled(referenceQueue.Width, scale), Scaled(referenceQueue.Height, scale));

        var regions = new Dictionary<string, RegionRect>();
        foreach (var name in RegionNames.All)
        {
            regions[name] = boxes.TryGetValue(name, out var box)
                ? new RegionRect((double)box.X / screen.Width, (double)box.Y / screen.Height, (double)box.Width / screen.Width, (double)box.Height / screen.Height).Clamped()
                : reference.Regions[name];
        }
        return new AutoCalibrationResult(new RegionLayout(screen.Width, screen.Height, regions), scale, [.. squares.Select(s => s.Rect)], clock.Rect);
    }

    /// <summary>Para diagnosticar: cada bloque negro de la franja de arriba, con si tiene la franja de color del mod encima y cuánto negro tiene.</summary>
    public static IReadOnlyList<(PixelRect Rect, bool Band, double Black)> Candidates(CapturedImage screen)
    {
        var strip = Strip(screen);
        return [.. Bodies(strip).Select(body => (body.Rect, ModPanel.HasBandAbove(strip, body.Tuple), BlackFraction(strip, body)))];
    }

    private static CapturedImage Strip(CapturedImage screen) =>
        screen.Crop(new PixelRect(0, 0, Math.Max(1, (int)(screen.Width * LeftPart)), Math.Max(1, (int)(screen.Height * TopBand))));

    /// <summary>El cuerpo negro de cada tramo de columnas seguidas con negro del mod (cuadrados, fondos de stock, "N/M", edad, reloj).</summary>
    private static IEnumerable<Body> Bodies(CapturedImage strip)
    {
        for (var x = 0; x < strip.Width; x++)
        {
            if (!IsModColumn(strip, x))
                continue;
            var start = x;
            while (x < strip.Width && IsModColumn(strip, x))
                x++;
            var (left, right, top, height) = ModPanel.Body(strip, (start, x - start));
            yield return new Body(left, right, top, height);
        }
    }

    private static bool IsModColumn(CapturedImage strip, int x)
    {
        var black = 0;
        for (var y = 0; y < strip.Height && black < ColumnBlack; y++)
        {
            var (r, g, b) = strip.Pixel(x, y);
            if (r <= ModPanel.Black && g <= ModPanel.Black && b <= ModPanel.Black)
                black++;
        }
        return black >= ColumnBlack;
    }

    private static double BlackFraction(CapturedImage strip, Body body)
    {
        var black = 0;
        for (var y = body.Top; y < body.Top + body.Height; y++)
        {
            for (var x = body.Left; x <= body.Right; x++)
            {
                var (r, g, b) = strip.Pixel(x, y);
                if (r <= ModPanel.Black && g <= ModPanel.Black && b <= ModPanel.Black)
                    black++;
            }
        }
        return (double)black / (body.Width * body.Height);
    }

    /// <summary>Dónde empieza la franja de color arriba del cuerpo negro (ver <see cref="ModPanel.Band"/>).</summary>
    private static int BandTop(CapturedImage image, Body square)
    {
        var x = square.Left + Math.Max(1, square.Width / 20);
        var top = Math.Max(0, square.Top - 2);
        var color = image.Pixel(x, top);
        while (top > 0 && ModPanel.Similar(image.Pixel(x, top - 1), color))
            top--;
        return top;
    }

    private static int Scaled(int pixels, double scale) => (int)Math.Round(pixels * scale);

    private readonly record struct Body(int Left, int Right, int Top, int Height)
    {
        public int Width => Right - Left + 1;
        public PixelRect Rect => new(Left, Top, Width, Height);
        public (int Left, int Right, int Top, int Height) Tuple => (Left, Right, Top, Height);
    }
}
