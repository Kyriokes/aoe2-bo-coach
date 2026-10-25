using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Pantallas y cajas sintéticas dibujadas desde el diseño (data/hud_layout.json), con los colores exactos que el mod
/// pone en el juego: sirven para probar la calibración sola y el lector sin una grabación.
/// </summary>
internal static class HudLayoutScreens
{
    public static readonly (byte R, byte G, byte B) Grass = (79, 107, 58);
    private static readonly (byte R, byte G, byte B) IconGray = (70, 70, 70);
    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    /// <summary>Marco del casillero que la GD de la app deja libre (como el overlay, al menos 3 px).</summary>
    public const int GdMargin = 3;

    /// <summary>
    /// Una pantalla entera con la barra del diseño en (<paramref name="originX"/>, <paramref name="originY"/>): fondo
    /// verde, barra, borde, casilleros, fondos negros, cuadrados grises por ícono, tres rayas blancas en cada campo
    /// (números cualquiera) y el rectángulo negro del reloj. Con <paramref name="gdInside"/>, la GD de la app tapa el
    /// medio de cada casillero (negro, dejando <see cref="GdMargin"/> px de borde).
    /// </summary>
    public static CapturedImage Render(HudLayout layout, double pixelsPerUnit, int width = 1920, int height = 1080, int originX = 0, int originY = 0, bool gdInside = false)
    {
        var canvas = new Canvas(width, height, Grass);
        PixelRect Rect(int x, int y, int w, int h) => new(originX + Px(x, pixelsPerUnit), originY + Px(y, pixelsPerUnit), Px(w, pixelsPerUnit), Px(h, pixelsPerUnit));
        DrawBar(canvas, layout, Rect);
        foreach (var cell in layout.Cells)
            DrawCell(canvas, layout, cell, Rect, gdInside, stripes: true);
        var clock = layout.ClockRect();
        canvas.Fill(Rect(clock.X, clock.Y, clock.Width, clock.Height), Black);
        return canvas.Image;
    }

    /// <summary>
    /// Una celda sola, como la graba el recorder: la caja de la celda con sus textos pintados con las plantillas de
    /// <paramref name="font"/> (misma letra del juego a 48 pt). <paramref name="darken"/> multiplica los canales
    /// (0,2 = partida en pausa).
    /// </summary>
    public static CapturedImage Box(HudLayout layout, HudCell cell, double pixelsPerUnit, IReadOnlyDictionary<string, string> texts, GlyphFont font, double darken = 1, bool chip = true)
    {
        var canvas = new Canvas(Px(cell.Width, pixelsPerUnit), Px(layout.Bar.Height, pixelsPerUnit), ToBytes(layout.Bar.Background));
        PixelRect Rect(int x, int y, int w, int h) => new(Px(x - cell.X, pixelsPerUnit), Px(y, pixelsPerUnit), Px(w, pixelsPerUnit), Px(h, pixelsPerUnit));
        DrawCell(canvas, layout, cell, Rect, gdInside: false, stripes: false, chip: chip);
        foreach (var (name, field) in cell.Fields)
        {
            if (texts.TryGetValue(name, out var text))
                canvas.Text(text, Rect(cell.X + field.X, field.Y, field.Width, field.Height), font, field.Points, pixelsPerUnit, White);
        }
        if (darken < 1)
            canvas.Darken(darken);
        return canvas.Image;
    }

    private static void DrawBar(Canvas canvas, HudLayout layout, Func<int, int, int, int, PixelRect> rect)
    {
        var bar = layout.Bar;
        canvas.Fill(rect(0, 0, bar.Width, bar.Height), ToBytes(bar.Background));
        var border = ToBytes(bar.BorderColor);
        canvas.Fill(rect(0, 0, bar.Width, bar.BorderWidth), border);
        canvas.Fill(rect(0, bar.Height - bar.BorderWidth, bar.Width, bar.BorderWidth), border);
        canvas.Fill(rect(0, 0, bar.BorderWidth, bar.Height), border);
        canvas.Fill(rect(bar.Width - bar.BorderWidth, 0, bar.BorderWidth, bar.Height), border);
    }

    private static void DrawCell(Canvas canvas, HudLayout layout, HudCell cell, Func<int, int, int, int, PixelRect> rect, bool gdInside, bool stripes, bool chip = true)
    {
        canvas.Fill(rect(cell.X + cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size), IconGray);
        if (cell.Chip is { } c && chip)
        {
            var r = rect(cell.X + c.X, c.Y, c.Width, c.Height);
            canvas.Fill(r, ToBytes(c.Color));
            if (gdInside)
                canvas.Fill(new PixelRect(r.X + GdMargin, r.Y + GdMargin, r.Width - 2 * GdMargin, r.Height - 2 * GdMargin), Black);
        }
        foreach (var b in cell.Backgrounds)
            canvas.Fill(rect(cell.X + b.X, b.Y, b.Width, b.Height), Black);
        if (!stripes)
            return;
        foreach (var field in cell.Fields.Values)
        {
            var r = rect(cell.X + field.X, field.Y, field.Width, field.Height);
            for (var i = 1; i <= 3; i++)
                canvas.Fill(new PixelRect(r.X + i * r.Width / 4, r.Y + r.Height / 4, 2, r.Height / 2), White);
        }
    }

    private static int Px(int units, double pixelsPerUnit) => (int)Math.Round(units * pixelsPerUnit);

    private static (byte R, byte G, byte B) ToBytes(HudColor color)
    {
        var (r, g, b) = color.Captured;
        return ((byte)r, (byte)g, (byte)b);
    }

    private sealed class Canvas
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _bgra;

        public Canvas(int width, int height, (byte R, byte G, byte B) background)
        {
            _width = width;
            _height = height;
            _bgra = new byte[width * height * 4];
            for (var i = 0; i < _bgra.Length; i += 4)
                (_bgra[i], _bgra[i + 1], _bgra[i + 2], _bgra[i + 3]) = (background.B, background.G, background.R, 255);
        }

        public CapturedImage Image => new(_width, _height, _bgra);

        public void Fill(PixelRect rect, (byte R, byte G, byte B) color)
        {
            for (var y = Math.Max(0, rect.Y); y < Math.Min(_height, rect.Y + rect.Height); y++)
                for (var x = Math.Max(0, rect.X); x < Math.Min(_width, rect.X + rect.Width); x++)
                    Set(x, y, color);
        }

        public void Darken(double factor)
        {
            for (var i = 0; i < _bgra.Length; i++)
                if (i % 4 != 3)
                    _bgra[i] = (byte)Math.Round(_bgra[i] * factor);
        }

        /// <summary>Un texto centrado en el campo con las plantillas de la letra, cada glifo de 0,73 × puntos × píxeles por unidad de alto.</summary>
        public void Text(string text, PixelRect field, GlyphFont font, int points, double ppu, (byte R, byte G, byte B) color)
        {
            var glyphHeight = Math.Max(2, (int)Math.Round(0.73 * points * ppu));
            var glyphWidth = Math.Max(1, (int)Math.Round(glyphHeight * font.TypicalWidthRatio));
            var spacing = Math.Max(1, glyphHeight / 8);
            var total = text.Length * glyphWidth + (text.Length - 1) * spacing;
            var x = field.X + (field.Width - total) / 2;
            var y = field.Y + (field.Height - glyphHeight) / 2;
            foreach (var c in text)
            {
                if (font.Templates.FirstOrDefault(t => t.Char == c) is { } template)
                    Glyph(template, x, y, glyphWidth, glyphHeight, color);
                x += glyphWidth + spacing;
            }
        }

        private void Glyph(GlyphTemplate template, int left, int top, int width, int height, (byte R, byte G, byte B) color)
        {
            for (var py = 0; py < height; py++)
            {
                var cy = Math.Min(GlyphFont.CellHeight - 1, py * GlyphFont.CellHeight / height);
                for (var px = 0; px < width; px++)
                {
                    var cx = Math.Min(GlyphFont.CellWidth - 1, px * GlyphFont.CellWidth / width);
                    if (template.Cell[cy * GlyphFont.CellWidth + cx] > 0.5f)
                        Set(left + px, top + py, color);
                }
            }
        }

        private void Set(int x, int y, (byte R, byte G, byte B) color)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return;
            var i = (y * _width + x) * 4;
            (_bgra[i], _bgra[i + 1], _bgra[i + 2], _bgra[i + 3]) = (color.B, color.G, color.R, 255);
        }
    }
}
