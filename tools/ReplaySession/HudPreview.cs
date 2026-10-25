using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Hud;

/// <summary>
/// hud-preview: dibuja el diseño del panel (data/hud_layout.json) como imagen, para retocar medidas sin abrir el juego.
/// Barra, borde, un cuadrado gris por ícono, casilleros, fondos negros, separador y números de muestra pintados con
/// las plantillas de las letras del mod (mod_numbers, mod_population para el "/", mod_clock), si están.
/// </summary>
internal static class HudPreview
{
    private static readonly (byte R, byte G, byte B) Grass = (79, 107, 58);
    private static readonly (byte R, byte G, byte B) IconGray = (70, 70, 70);
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);
    private static readonly (byte R, byte G, byte B) SeparatorGray = (140, 140, 140);

    private static readonly Dictionary<string, Dictionary<string, string>> Samples = new()
    {
        ["wood"] = new() { ["villagers"] = "12", ["stock"] = "1250" },
        ["food"] = new() { ["villagers"] = "9", ["stock"] = "386" },
        ["gold"] = new() { ["villagers"] = "4", ["stock"] = "10000" },
        ["stone"] = new() { ["villagers"] = "0", ["stock"] = "200" },
        ["population"] = new() { ["total"] = "19", ["cap"] = "21/30" },
        ["idle"] = new() { ["count"] = "3" },
        ["age"] = new(),
    };

    public static int Render(string output, HudLayout layout, string fontsDir, double scale)
    {
        var ppu = layout.PixelsPerUnitAt1080p * scale;
        int Px(double units) => (int)Math.Round(units * ppu);
        const int margin = 20;
        var width = Px(layout.Bar.Width + layout.Clock.Gap + layout.Clock.Width + 2 * margin);
        var height = Px(layout.Bar.Height + 2 * margin);
        var canvas = new Canvas(width, height, Grass);
        var numbers = Optional(Path.Combine(fontsDir, "mod_numbers.json"));
        var population = Optional(Path.Combine(fontsDir, "mod_population.json"));
        var clockFont = Optional(Path.Combine(fontsDir, "mod_clock.json"));

        PixelRect Rect(int x, int y, int w, int h) => new(Px(margin + x), Px(margin + y), Px(w), Px(h));
        var bar = layout.Bar;
        canvas.Fill(Rect(0, 0, bar.Width, bar.Height), ToBytes(bar.Background));
        var border = ToBytes(bar.BorderColor);
        canvas.Fill(Rect(0, 0, bar.Width, bar.BorderWidth), border);
        canvas.Fill(Rect(0, bar.Height - bar.BorderWidth, bar.Width, bar.BorderWidth), border);
        canvas.Fill(Rect(0, 0, bar.BorderWidth, bar.Height), border);
        canvas.Fill(Rect(bar.Width - bar.BorderWidth, 0, bar.BorderWidth, bar.Height), border);

        foreach (var cell in layout.Cells)
        {
            canvas.Fill(Rect(cell.X + cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size), IconGray);
            if (cell.Chip is { } chip)
                canvas.Fill(Rect(cell.X + chip.X, chip.Y, chip.Width, chip.Height), ToBytes(chip.Color));
            foreach (var b in cell.Backgrounds)
                canvas.Fill(Rect(cell.X + b.X, b.Y, b.Width, b.Height), (0, 0, 0));
            if (cell.Separator is { } s)
            {
                var rect = Rect(cell.X + s.X, s.Y, s.Width, s.Height);
                if (s.Text is { Length: > 0 } text && population is not null)
                    canvas.Text(text, rect, population, s.Points, ppu, ToBytes(s.Color));
                else
                    canvas.Fill(Rect(cell.X + s.X + (s.Width - 4) / 2, s.Y, 4, s.Height), SeparatorGray);
            }
            foreach (var (name, field) in cell.Fields)
            {
                if (!Samples[cell.Name].TryGetValue(name, out var sample))
                    continue;
                var font = sample.Contains('/') ? population : numbers;
                if (font is not null)
                    canvas.Text(sample, Rect(cell.X + field.X, field.Y, field.Width, field.Height), font, field.Points, ppu, White);
            }
        }
        var clock = layout.ClockRect();
        var clockRect = Rect(clock.X, clock.Y, clock.Width, clock.Height);
        canvas.Fill(clockRect, (0, 0, 0));
        if (clockFont is not null)
            canvas.Text("00:12:34", clockRect, clockFont, layout.Clock.Points, ppu, White);

        Png.Write(canvas.Image, output);
        Console.WriteLine($"Guardado {output}: {width}×{height} px (escala {scale:0.##}; diseño {layout.Name}).");
        return 0;
    }

    private static GlyphFont? Optional(string path) => File.Exists(path) ? GlyphFont.Load(path) : null;

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

        /// <summary>
        /// Un texto centrado en el campo con las plantillas de la letra: cada glifo mide 0,73 × puntos × píxeles por
        /// unidad de alto (lo que mide un dígito del juego) y el ancho típico de la letra.
        /// </summary>
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
