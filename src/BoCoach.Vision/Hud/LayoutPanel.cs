using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Hud;

/// <summary>
/// Con el diseño (<see cref="HudLayout"/>), cada caja grabada es una celda entera: el casillero de color está donde el
/// diseño dice (se verifica por su color, por los bordes: la GD de la app tapa el medio) y cada campo se recorta en
/// proporción, corrido con su fondo negro si la caja quedó unos píxeles desplazada.
/// </summary>
public static class LayoutPanel
{
    /// <summary>Tolerancia de color del casillero, por canal.</summary>
    public const int ChipTolerance = 45;

    /// <summary>Hasta cuántos píxeles (a 1080p) se corre un campo para seguir a su fondo negro.</summary>
    public const int Refine = 3;

    /// <summary>Parte de cada línea del borde del casillero que tiene que ser de su color.</summary>
    public const double LineMatch = 0.8;

    /// <summary>
    /// El rectángulo del casillero si su color está en el lugar que dice el diseño (las cuatro líneas a 1 px de su
    /// borde, dentro del marco que la GD de la app deja libre); null si no está (mod viejo, sin mod, caja corrida).
    /// Una celda sin casillero (inactivos) devuelve la caja entera: ahí dibuja la app su marco.
    /// </summary>
    public static PixelRect? Chip(HudLayout layout, HudCell cell, CapturedImage box)
    {
        if (cell.Chip is not { } chip)
            return new PixelRect(0, 0, box.Width, box.Height);
        var rect = layout.ChipRect(cell, new PixelRect(0, 0, box.Width, box.Height));
        var inner = new PixelRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
        if (inner.Width < 2 || inner.Height < 2 || inner.X + inner.Width > box.Width || inner.Y + inner.Height > box.Height)
            return null;
        var color = chip.Color.Captured;
        // Cada línea tiene que ser del color del casillero en su mayor parte: el total de población va escrito sobre
        // el casillero y puede tocarlas, y los bordes del juego salen suavizados. Un casillero corrido 2 px o más
        // deja una línea entera afuera.
        bool Line(IEnumerable<(int X, int Y)> pixels)
        {
            int total = 0, matching = 0;
            foreach (var (x, y) in pixels)
            {
                total++;
                if (Similar(box.Pixel(x, y), color))
                    matching++;
            }
            return matching >= LineMatch * total;
        }
        var columns = Enumerable.Range(inner.X, inner.Width);
        var rows = Enumerable.Range(inner.Y, inner.Height);
        if (!Line(columns.Select(x => (x, inner.Y))) || !Line(columns.Select(x => (x, inner.Y + inner.Height - 1)))
            || !Line(rows.Select(y => (inner.X, y))) || !Line(rows.Select(y => (inner.X + inner.Width - 1, y))))
            return null;
        return rect;
    }

    /// <summary>
    /// Dónde está un campo dentro de la caja: en proporción y, con <paramref name="refine"/>, corrido junto con su fondo
    /// negro (los bordes del fondo se buscan hasta <see cref="Refine"/> píxeles hacia adentro o hacia afuera).
    /// </summary>
    public static PixelRect Field(HudLayout layout, HudCell cell, string field, CapturedImage box, bool refine = true)
    {
        var whole = new PixelRect(0, 0, box.Width, box.Height);
        var rect = layout.FieldRect(cell, field, box: whole);
        if (!refine || BackgroundOf(cell, cell.Fields[field]) is not { } background)
            return rect;
        var b = Proportional(layout, cell, background, whole);
        var dx = EdgeShift(x => BlackColumn(box, x, b.Y + 1, b.Height - 2), b.X, +1);
        var dy = EdgeShift(y => BlackRow(box, y, b.X + 1, b.Width - 2), b.Y, +1);
        return rect with { X = rect.X + dx, Y = rect.Y + dy };
    }

    /// <summary>El campo recortado de la caja (el total de población va sobre el casillero: sin afinar).</summary>
    public static CapturedImage Crop(HudLayout layout, HudCell cell, string field, CapturedImage box) =>
        box.Crop(Clamp(Field(layout, cell, field, box, refine: field != "total"), box));

    /// <summary>
    /// Cuánto se corrió un borde: hacia adentro mientras la línea del borde no sea negra, hacia afuera mientras la
    /// línea anterior sí lo sea, nunca más de <see cref="Refine"/>.
    /// </summary>
    private static int EdgeShift(Func<int, bool> isBlack, int edge, int inward)
    {
        var shift = 0;
        while (shift < Refine && !isBlack(edge + shift))
            shift++;
        if (shift > 0)
            return shift * inward;
        while (shift < Refine && isBlack(edge - (shift + 1)))
            shift++;
        return -shift * inward;
    }

    private static UnitRect? BackgroundOf(HudCell cell, HudField field) =>
        cell.Backgrounds.FirstOrDefault(b => field.X >= b.X && field.Y >= b.Y && field.X + field.Width <= b.X + b.Width && field.Y + field.Height <= b.Y + b.Height);

    private static PixelRect Proportional(HudLayout layout, HudCell cell, UnitRect r, PixelRect box) => new(
        box.X + Scale(r.X, box.Width, cell.Width),
        box.Y + Scale(r.Y, box.Height, layout.Bar.Height),
        Scale(r.Width, box.Width, cell.Width),
        Scale(r.Height, box.Height, layout.Bar.Height));

    private static int Scale(int units, int pixels, int totalUnits) => (int)Math.Round((double)units * pixels / totalUnits);

    private static bool BlackColumn(CapturedImage box, int x, int top, int height)
    {
        if (x < 0 || x >= box.Width || height <= 0)
            return false;
        var black = 0;
        for (var y = Math.Max(0, top); y < Math.Min(box.Height, top + height); y++)
            if (IsBlack(box.Pixel(x, y)))
                black++;
        return black >= height * 0.9;
    }

    private static bool BlackRow(CapturedImage box, int y, int left, int width)
    {
        if (y < 0 || y >= box.Height || width <= 0)
            return false;
        var black = 0;
        for (var x = Math.Max(0, left); x < Math.Min(box.Width, left + width); x++)
            if (IsBlack(box.Pixel(x, y)))
                black++;
        return black >= width * 0.9;
    }

    private static bool IsBlack((byte R, byte G, byte B) p) => p.R <= ModPanel.Black && p.G <= ModPanel.Black && p.B <= ModPanel.Black;

    private static bool Similar((byte R, byte G, byte B) p, (int R, int G, int B) c) =>
        Math.Abs(p.R - c.R) <= ChipTolerance && Math.Abs(p.G - c.G) <= ChipTolerance && Math.Abs(p.B - c.B) <= ChipTolerance;

    private static PixelRect Clamp(PixelRect r, CapturedImage box)
    {
        var x = Math.Clamp(r.X, 0, Math.Max(0, box.Width - 1));
        var y = Math.Clamp(r.Y, 0, Math.Max(0, box.Height - 1));
        return new PixelRect(x, y, Math.Clamp(r.Width, 1, box.Width - x), Math.Clamp(r.Height, 1, box.Height - y));
    }
}
