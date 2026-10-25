using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Regions;

/// <param name="Layout">Las regiones para la pantalla capturada (resolución de referencia = la de la captura).</param>
/// <param name="Scale">Tamaño del HUD respecto de 1920×1080 (1 = igual; las letras y los íconos están medidos a 1080p).</param>
/// <param name="Squares">Los cuadrados del mod (madera, comida, oro, piedra, población, inactivos), de izquierda a derecha.</param>
/// <param name="Clock">La caja del reloj.</param>
/// <param name="Hud">El diseño del mod con que se calibró (los casilleros son las anclas), o null con el mod viejo.</param>
public sealed record AutoCalibrationResult(RegionLayout Layout, double Scale, IReadOnlyList<PixelRect> Squares, PixelRect Clock, HudLayout? Hud = null);

/// <summary>
/// Calibración sola, con el mod BoCoach HUD. Las anclas son las seis franjas de color de los cuadrados (marrón de la
/// madera, rosa de la comida, amarilla del oro, gris de la piedra, azul de la población y roja de los inactivos): se
/// ven con cualquier fondo de panel (el de otras civs es negro, igual que los cuadrados) y a cualquier escala. El
/// cuadrado mide lo que mide su franja de ancho, y el número de aldeanos está en lo que queda debajo. El reloj y la
/// edad son los dos textos claros que siguen a la derecha de los inactivos (primero la edad, después el reloj). De
/// ahí salen las cajas como las dibujaría la calibración manual; la cola de producción se ubica con la escala, y el
/// log y los puntajes quedan donde dice la calibración de referencia (van pegados a los bordes de la pantalla).
/// </summary>
public static class AutoCalibration
{
    /// <summary>Parte de arriba de la pantalla donde está el panel de recursos (a 1080p, 108 px: los cuadrados van de la fila 7 a la 43).</summary>
    private const double TopBand = 0.10;

    /// <summary>Parte de la pantalla, desde la izquierda, donde entran los cuadrados, la edad y el reloj (mod viejo).</summary>
    private const double LeftPart = 0.6;

    /// <summary>Con el diseño la barra es más ancha: a escala 100 % los casilleros llegan al 40 % y el reloj al 75 %.</summary>
    private const double LayoutLeftPart = 0.8;

    /// <summary>Tolerancia de la calibración con el diseño, en píxeles a 1080p: cada casillero medido contra donde el diseño lo pone.</summary>
    private const int LayoutTolerance = 3;

    /// <summary>Hasta dónde se busca el rectángulo negro del reloj alrededor de donde el diseño lo predice, en píxeles a 1080p.</summary>
    private const int ClockSearch = 40;

    /// <summary>Ancho de un cuadrado (y de su franja) a 1920×1080, medido en la grabación 20261018-132830.</summary>
    public const double ReferenceSquareWidth = 36;

    /// <summary>Ancho de la caja del reloj a 1080p: entra "hh:mm:ss" y nada más.</summary>
    private const double ReferenceClockWidth = 79;

    /// <summary>Los colores de las franjas del mod, en el orden en que están en el panel.</summary>
    private static readonly (string Region, (int R, int G, int B) Color)[] BandColors =
    [
        (RegionNames.Wood, (135, 91, 42)),
        (RegionNames.Food, (225, 104, 106)),
        (RegionNames.Gold, (240, 192, 25)),
        (RegionNames.Stone, (158, 158, 167)),
        (RegionNames.Population, (62, 128, 254)),
        (RegionNames.IdleVillagers, (230, 0, 0)),
    ];

    private const int ColorTolerance = 45;

    /// <summary>Dos píxeles seguidos de una franja difieren menos que esto por canal (la captura es lisa; el WebP de un chat, casi).</summary>
    private const int Even = 14;

    private const int MinBandWidth = 16;

    /// <summary>Canal más bajo de un píxel blanco (los dígitos del reloj, "Dark Age"); los adornos grises del marco quedan abajo.</summary>
    private const int White = 215;

    // Márgenes como los de la calibración manual del usuario a 1080p: 1 a la izquierda, 2 arriba de la franja, 3 debajo
    // del cuadrado y 4 entre una caja y el cuadrado siguiente. Van con la escala.
    private const int LeftMargin = 1;
    private const int TopMargin = 2;
    private const int BottomMargin = 3;
    private const int Gap = 4;

    /// <param name="layout">El diseño del mod nuevo: con él se prueban primero los casilleros; si no están, las seis franjas del mod viejo.</param>
    public static AutoCalibrationResult? Detect(CapturedImage screen, RegionLayout reference, HudLayout? layout = null)
    {
        if (layout is not null && DetectLayout(screen, reference, layout) is { } withLayout)
            return withLayout;
        return DetectBands(screen, reference);
    }

    /// <summary>El mod viejo: seis franjas de color sobre cuadrados negros.</summary>
    private static AutoCalibrationResult? DetectBands(CapturedImage screen, RegionLayout reference)
    {
        var strip = Strip(screen, LeftPart);
        var bands = FindBands(strip);
        if (bands is null)
            return null;
        var scale = bands.Average(b => b.Width) / ReferenceSquareWidth;
        var squares = bands.Select(b => new PixelRect(b.X, b.Y, b.Width, b.Width)).ToList();
        var idle = squares[5];

        // La edad y el reloj: los dos textos claros a la derecha de los inactivos, a la altura de los cuadrados.
        var texts = TextClusters(strip, idle.X + idle.Width + Scaled(Gap, scale), idle.Y, idle.Height, scale);
        // Texto de un renglón: ni el medallón de la edad (ocupa todo el alto) ni una raya. Primero la edad, después el reloj.
        texts = texts.Where(t => t.Height >= idle.Height * 0.25 && t.Height <= idle.Height * 0.75).ToList();
        if (texts.Count < 2)
            return null;
        var (age, clockText) = (texts[0], texts[1]);

        var (leftMargin, topMargin, bottomMargin, gap) = (Scaled(LeftMargin, scale), Scaled(TopMargin, scale), Scaled(BottomMargin, scale), Scaled(Gap, scale));
        var boxes = new Dictionary<string, PixelRect>();
        for (var i = 0; i < 6; i++)
        {
            var square = squares[i];
            var top = square.Y - topMargin;
            var left = square.X - leftMargin;
            var right = i < 5 ? squares[i + 1].X - gap : square.X + square.Width + leftMargin;
            var bottom = square.Y + square.Height + bottomMargin;
            boxes[BandColors[i].Region] = new PixelRect(left, top, right - left, bottom - top);
        }
        boxes[RegionNames.Clock] = new PixelRect(clockText.X - topMargin, clockText.Y - topMargin, Scaled((int)ReferenceClockWidth, scale), clockText.Height + 2 * topMargin);
        boxes[RegionNames.Age] = new PixelRect(age.X - 3 * topMargin, age.Y - 2 * topMargin, age.Width + 6 * topMargin, age.Height + 4 * topMargin);
        return Finish(screen, reference, boxes, scale, squares, null);
    }

    /// <summary>
    /// El mod nuevo: los casilleros de color del diseño son las anclas. Factor píxeles por unidad = mediana de los
    /// anchos medidos sobre los del diseño; origen de la barra = mediana de lo que cada casillero dista de su lugar.
    /// Si un casillero no cae donde el diseño lo pone (a 3 px a 1080p, o con otro ancho), no hay detección.
    /// </summary>
    private static AutoCalibrationResult? DetectLayout(CapturedImage screen, RegionLayout reference, HudLayout layout)
    {
        var strip = Strip(screen, LayoutLeftPart);
        var rects = EvenRects(strip);
        var anchors = new List<(HudCell Cell, PixelRect Rect)>();
        foreach (var cell in layout.Anchors)
        {
            var color = cell.Chip!.Color.Captured;
            var candidates = rects.Where(r => Within(r.Color, color, ColorTolerance)).ToList();
            if (candidates.Count == 0)
                return null;
            // El más ancho es el casillero (o su parte libre: la GD de la app lo parte en varios rectángulos del mismo ancho).
            var widest = candidates.MaxBy(r => r.Rect.Width);
            var same = candidates.Where(r => Math.Abs(r.Rect.X - widest.Rect.X) <= 2 && Math.Abs(r.Rect.X + r.Rect.Width - widest.Rect.X - widest.Rect.Width) <= 2).ToList();
            var top = same.Min(r => r.Rect.Y);
            var bottom = same.Max(r => r.Rect.Y + r.Rect.Height);
            (top, bottom) = Extend(strip, widest.Rect.X + 1, widest.Rect.X + widest.Rect.Width - 2, top, bottom, widest.Color);
            anchors.Add((cell, new PixelRect(widest.Rect.X, top, widest.Rect.Width, bottom - top)));
        }
        var factor = Median(anchors.Select(a => (double)a.Rect.Width / a.Cell.Chip!.Width));
        if (factor <= 0)
            return null;
        var originX = Median(anchors.Select(a => a.Rect.X - (a.Cell.X + a.Cell.Chip!.X) * factor));
        var originY = Median(anchors.Select(a => a.Rect.Y - a.Cell.Chip!.Y * factor));
        var scale = factor / layout.PixelsPerUnitAt1080p;
        var tolerance = Math.Max(1, Scaled(LayoutTolerance, scale));
        foreach (var (cell, rect) in anchors)
        {
            var chip = cell.Chip!;
            if (Math.Abs(rect.X - (originX + (cell.X + chip.X) * factor)) > tolerance
                || Math.Abs(rect.Y - (originY + chip.Y * factor)) > tolerance
                || Math.Abs(rect.Width - chip.Width * factor) > 0.1 * chip.Width * factor)
                return null;
        }

        PixelRect Box(UnitRect r) => new(
            (int)Math.Round(originX + r.X * factor), (int)Math.Round(originY + r.Y * factor), (int)Math.Round(r.Width * factor), (int)Math.Round(r.Height * factor));
        var boxes = new Dictionary<string, PixelRect>();
        foreach (var cell in layout.Cells)
            boxes[HudLayout.RegionOf(cell.Name)] = cell.Name == "age" ? Box(layout.AgeTextRect()) : Box(layout.CellRect(cell));
        boxes[RegionNames.Clock] = ClockBox(screen, Box(layout.ClockRect()), Scaled(ClockSearch, scale));
        return Finish(screen, reference, boxes, scale, anchors.Select(a => a.Rect).ToList(), layout);
    }

    /// <summary>
    /// El reloj está en otro panel del juego y su ancla puede no caer exactamente donde el diseño lo predice: se busca
    /// la primera columna con el 80 % de negro puro en las filas del reloj, hasta <paramref name="search"/> px a cada lado.
    /// </summary>
    private static PixelRect ClockBox(CapturedImage screen, PixelRect predicted, int search)
    {
        var top = predicted.Y + 2;
        var height = predicted.Height - 4;
        if (height <= 0 || top < 0 || top + height > screen.Height)
            return predicted;
        for (var x = Math.Max(0, predicted.X - search); x <= Math.Min(screen.Width - 1, predicted.X + search); x++)
        {
            var black = 0;
            for (var y = top; y < top + height; y++)
            {
                var (r, g, b) = screen.Pixel(x, y);
                if (r <= ModPanel.Black && g <= ModPanel.Black && b <= ModPanel.Black)
                    black++;
            }
            if (black >= height * 0.8)
                return predicted with { X = x };
        }
        return predicted;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    private static bool Within((int R, int G, int B) a, (int R, int G, int B) b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>Lo común a los dos caminos: cola de producción, log y puntajes, y el RegionLayout.</summary>
    private static AutoCalibrationResult Finish(CapturedImage screen, RegionLayout reference, Dictionary<string, PixelRect> boxes, double scale, IReadOnlyList<PixelRect> squares, HudLayout? hud)
    {
        var referenceQueue = reference.PixelsOf(RegionNames.ProductionQueue, 1920, 1080);
        boxes[RegionNames.ProductionQueue] = new PixelRect(
            Scaled(referenceQueue.X, scale), Scaled(referenceQueue.Y, scale), Scaled(referenceQueue.Width, scale), Scaled(referenceQueue.Height, scale));

        // El panel de puntajes, anclado abajo a la derecha de la pantalla y escalado como el HUD (la caja guardada podía
        // ser la vieja, de 2 renglones).
        var scores = RegionLayout.Default.PixelsOf(RegionNames.Scores, 1920, 1080);
        var scoresWidth = Scaled(scores.Width, scale);
        var scoresHeight = Scaled(scores.Height, scale);
        boxes[RegionNames.Scores] = new PixelRect(
            screen.Width - Scaled(1920 - scores.X, scale), screen.Height - Scaled(1080 - scores.Y, scale), scoresWidth, scoresHeight);

        var regions = new Dictionary<string, RegionRect>();
        foreach (var name in RegionNames.All)
        {
            regions[name] = boxes.TryGetValue(name, out var box)
                ? new RegionRect((double)box.X / screen.Width, (double)box.Y / screen.Height, (double)box.Width / screen.Width, (double)box.Height / screen.Height).Clamped()
                : reference.Regions[name];
        }
        return new AutoCalibrationResult(new RegionLayout(screen.Width, screen.Height, regions, scale, hud), scale, squares, boxes[RegionNames.Clock], hud);
    }

    /// <summary>Para diagnosticar: cada rectángulo de color parejo de la franja de arriba y a qué franja del mod se parece (o null).</summary>
    public static IReadOnlyList<(PixelRect Rect, string? Band, (int R, int G, int B) Color)> Candidates(CapturedImage screen) =>
        [.. EvenRects(Strip(screen, LayoutLeftPart)).Select(r => (r.Rect, BandOf(r.Color)?.Region, r.Color))];

    private static CapturedImage Strip(CapturedImage screen, double leftPart) =>
        screen.Crop(new PixelRect(0, 0, Math.Max(1, (int)(screen.Width * leftPart)), Math.Max(1, (int)(screen.Height * TopBand))));

    /// <summary>
    /// Las seis franjas, de izquierda a derecha, o null si no están. La GD del overlay se dibuja dentro de la franja y
    /// la parte en varios rectángulos: se juntan los del mismo color y el mismo ancho. Una franja de cada color, con el
    /// orden del panel y anchos parecidos (la del medio manda).
    /// </summary>
    private static List<PixelRect>? FindBands(CapturedImage strip)
    {
        var bands = new List<PixelRect>();
        foreach (var (region, _) in BandColors)
        {
            var rects = EvenRects(strip).Where(r => BandOf(r.Color)?.Region == region).Select(r => r.Rect).ToList();
            if (rects.Count == 0)
                return null;
            // El rectángulo más ancho es la franja (o su parte libre); se le suman los que tienen el mismo ancho encima o debajo.
            var widest = rects.MaxBy(r => r.Width);
            var same = rects.Where(r => Math.Abs(r.X - widest.X) <= 2 && Math.Abs(r.X + r.Width - widest.X - widest.Width) <= 2).ToList();
            var top = same.Min(r => r.Y);
            var bottom = same.Max(r => r.Y + r.Height);
            // La GD del overlay tapa el medio de la franja: por los costados, que quedan libres, se llega al borde real.
            var color = rects.Count > 0 ? EvenRects(strip).First(r => r.Rect == widest).Color : default;
            (top, bottom) = Extend(strip, widest.X + 1, widest.X + widest.Width - 2, top, bottom, color);
            bands.Add(new PixelRect(widest.X, top, widest.Width, bottom - top));
        }
        var median = bands.OrderBy(b => b.Width).ElementAt(bands.Count / 2).Width;
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Width < median * 0.7 || bands[i].Width > median * 1.3)
                return null;
            if (i > 0 && bands[i].X <= bands[i - 1].X + bands[i - 1].Width)
                return null;
        }
        return bands;
    }

    /// <summary>Estira un rectángulo hacia arriba y abajo mientras las dos columnas sigan con el color de la franja.</summary>
    private static (int Top, int Bottom) Extend(CapturedImage strip, int leftColumn, int rightColumn, int top, int bottom, (int R, int G, int B) color)
    {
        bool Matches(int y) => Similar(ToInts(strip.Pixel(leftColumn, y)), color) && Similar(ToInts(strip.Pixel(rightColumn, y)), color);
        while (top > 0 && Matches(top - 1))
            top--;
        while (bottom < strip.Height && Matches(bottom))
            bottom++;
        return (top, bottom);
    }

    private static (int R, int G, int B) ToInts((byte R, byte G, byte B) p) => (p.R, p.G, p.B);

    /// <summary>Rectángulos de color parejo (no oscuro, no blanco) de al menos 3 filas y <see cref="MinBandWidth"/> columnas.</summary>
    private static List<(PixelRect Rect, (int R, int G, int B) Color)> EvenRects(CapturedImage strip)
    {
        var open = new List<(int Left, int Right, int Top, int Bottom, (int R, int G, int B) Color)>();
        var done = new List<(PixelRect Rect, (int R, int G, int B) Color)>();
        for (var y = 0; y < strip.Height; y++)
        {
            var row = new List<(int Left, int Right, (int R, int G, int B) Color)>();
            for (var x = 0; x < strip.Width;)
            {
                var color = strip.Pixel(x, y);
                if (!IsBandLike(color))
                {
                    x++;
                    continue;
                }
                var start = x;
                long r = 0, g = 0, b = 0;
                while (x < strip.Width && Similar(strip.Pixel(x, y), color))
                {
                    var p = strip.Pixel(x, y);
                    (r, g, b) = (r + p.R, g + p.G, b + p.B);
                    x++;
                }
                var length = x - start;
                if (length >= MinBandWidth)
                    row.Add((start, x - 1, ((int)(r / length), (int)(g / length), (int)(b / length))));
            }
            // Sigue un rectángulo abierto si el tramo es el mismo en la fila siguiente; si no, se cierra.
            var continued = new HashSet<int>();
            foreach (var run in row)
            {
                var index = open.FindIndex(o => o.Bottom == y - 1 && Math.Abs(o.Left - run.Left) <= 2 && Math.Abs(o.Right - run.Right) <= 2 && Similar(o.Color, run.Color));
                if (index >= 0)
                {
                    open[index] = open[index] with { Bottom = y };
                    continued.Add(index);
                }
                else
                {
                    open.Add((run.Left, run.Right, y, y, run.Color));
                    continued.Add(open.Count - 1);
                }
            }
            for (var i = open.Count - 1; i >= 0; i--)
            {
                if (continued.Contains(i) || open[i].Bottom == y)
                    continue;
                Close(open[i]);
                open.RemoveAt(i);
            }
        }
        foreach (var o in open)
            Close(o);
        return done;

        void Close((int Left, int Right, int Top, int Bottom, (int R, int G, int B) Color) o)
        {
            if (o.Bottom - o.Top + 1 >= 3)
                done.Add((new PixelRect(o.Left, o.Top, o.Right - o.Left + 1, o.Bottom - o.Top + 1), o.Color));
        }
    }

    /// <summary>
    /// Los textos claros (blanco del reloj, amarillo de la edad) a la derecha de <paramref name="fromX"/>, en las filas
    /// de los cuadrados, de izquierda a derecha: columnas con píxeles claros, juntando huecos chicos (entre letras).
    /// </summary>
    private static List<PixelRect> TextClusters(CapturedImage strip, int fromX, int top, int height, double scale)
    {
        var bottom = Math.Min(strip.Height, top + height);
        var maxGap = Scaled(24, scale); // entre palabras ("Alta Edad Media") hay menos; hasta el reloj, mucho más
        var clusters = new List<PixelRect>();
        var x = Math.Max(0, fromX);
        while (x < strip.Width)
        {
            if (!HasText(x))
            {
                x++;
                continue;
            }
            var start = x;
            var last = x;
            while (x < strip.Width && x - last <= maxGap)
            {
                if (HasText(x))
                    last = x;
                x++;
            }
            var (textTop, textBottom) = (int.MaxValue, -1);
            for (var xx = start; xx <= last; xx++)
                for (var y = top; y < bottom; y++)
                    if (IsText(strip.Pixel(xx, y)))
                        (textTop, textBottom) = (Math.Min(textTop, y), Math.Max(textBottom, y));
            clusters.Add(new PixelRect(start, textTop, last - start + 1, textBottom - textTop + 1));
        }
        return clusters;

        bool HasText(int column)
        {
            var count = 0;
            for (var y = top; y < bottom; y++)
                if (IsText(strip.Pixel(column, y)) && ++count >= 2)
                    return true;
            return false;
        }
    }

    private static (string Region, (int R, int G, int B) Color)? BandOf((int R, int G, int B) color)
    {
        foreach (var band in BandColors)
        {
            if (Math.Abs(band.Color.R - color.R) <= ColorTolerance && Math.Abs(band.Color.G - color.G) <= ColorTolerance && Math.Abs(band.Color.B - color.B) <= ColorTolerance)
                return band;
        }
        return null;
    }

    private static bool IsBandLike((byte R, byte G, byte B) p) => Math.Max(p.R, Math.Max(p.G, p.B)) >= 60 && Math.Min(p.R, Math.Min(p.G, p.B)) < 200;

    /// <summary>Blanco (reloj, "Dark Age") o amarillo ("Alta Edad Media" con el juego en castellano).</summary>
    private static bool IsText((byte R, byte G, byte B) p) =>
        Math.Min(p.R, Math.Min(p.G, p.B)) >= White || (p.R >= 200 && p.G >= 170 && p.B <= 120);

    private static bool Similar((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Abs(a.R - b.R) <= Even && Math.Abs(a.G - b.G) <= Even && Math.Abs(a.B - b.B) <= Even;

    private static bool Similar((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Abs(a.R - b.R) <= Even && Math.Abs(a.G - b.G) <= Even && Math.Abs(a.B - b.B) <= Even;

    private static int Scaled(int pixels, double scale) => (int)Math.Round(pixels * scale);
}
