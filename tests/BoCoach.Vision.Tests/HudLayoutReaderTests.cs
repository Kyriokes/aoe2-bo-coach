using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Digits;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Con el diseño (data/hud_layout.json) y su letra, el lector recorta cada campo por el diseño dentro de la caja de su
/// celda. Las cajas son sintéticas: los números van pintados con las plantillas de las letras del mod viejo, que son la
/// misma letra del juego a 48 pt, así que lo que se pinta con ellas se tiene que leer.
/// </summary>
public class HudLayoutReaderTests
{
    private static readonly HudLayout Layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));
    private static readonly string FontsDir = Path.Combine(AppContext.BaseDirectory, "fonts");
    private const double Ppu = 0.4286;

    /// <summary>La letra del diseño nuevo todavía no se armó: dígitos de mod_numbers (48 pt) y el "/" de mod_population.</summary>
    private static GlyphFont Numbers()
    {
        var numbers = GlyphFont.Load(Path.Combine(FontsDir, "mod_numbers.json"));
        var population = GlyphFont.Load(Path.Combine(FontsDir, "mod_population.json"));
        return new GlyphFont("hud_numbers", numbers.Ink, [.. numbers.Templates, .. population.Templates.Where(t => t.Char == '/')], numbers.TypicalWidthRatio);
    }

    private static HudFonts Fonts(bool withNumbers = true) => HudFonts.Load(FontsDir) with { HudNumbers = withNumbers ? Numbers() : null };

    private static Dictionary<string, CapturedImage> Boxes(
        (string Villagers, string Stock) wood, (string Villagers, string Stock) food, (string Villagers, string Stock) gold, (string Villagers, string Stock) stone,
        (string Total, string Cap) population, string idle, double darken = 1, bool woodChip = true)
    {
        var font = Numbers();
        CapturedImage Box(string cell, Dictionary<string, string> texts, bool chip = true) =>
            HudLayoutScreens.Box(Layout, Layout.Cell(cell), Ppu, texts, font, darken, chip);
        return new Dictionary<string, CapturedImage>
        {
            [RegionNames.Wood] = Box("wood", new() { ["villagers"] = wood.Villagers, ["stock"] = wood.Stock }, woodChip),
            [RegionNames.Food] = Box("food", new() { ["villagers"] = food.Villagers, ["stock"] = food.Stock }),
            [RegionNames.Gold] = Box("gold", new() { ["villagers"] = gold.Villagers, ["stock"] = gold.Stock }),
            [RegionNames.Stone] = Box("stone", new() { ["villagers"] = stone.Villagers, ["stock"] = stone.Stock }),
            [RegionNames.Population] = Box("population", new() { ["total"] = population.Total, ["cap"] = population.Cap }),
            [RegionNames.IdleVillagers] = Box("idle", new() { ["count"] = idle }),
        };
    }

    [Fact]
    public void Lee_los_campos_de_cada_celda_con_la_letra_del_mod()
    {
        var boxes = Boxes(("12", "1250"), ("9", "386"), ("4", "9999"), ("0", "200"), ("19", "21/30"), "3");

        var reading = new HudReader(Fonts()).Read(boxes.GetValueOrDefault, Layout);

        Assert.Equal("bocoach-2026-10", reading.LayoutName);
        Assert.Equal(1250, reading.Wood);
        Assert.Equal(12, reading.VillagersOn![Resource.Wood]);
        Assert.Equal(386, reading.Food);
        Assert.Equal(9, reading.VillagersOn[Resource.Food]);
        Assert.Equal(9999, reading.Gold);
        Assert.Equal(200, reading.Stone);
        Assert.Equal(0, reading.VillagersOn[Resource.Stone]);
        Assert.Equal(19, reading.Villagers);
        Assert.Equal((21, 30), (reading.Population, reading.PopulationCap));
        Assert.Equal(3, reading.IdleVillagers);
        var woodBox = boxes[RegionNames.Wood];
        Assert.Equal(Layout.ChipRect(Layout.Cell("wood"), new PixelRect(0, 0, woodBox.Width, woodBox.Height)), reading.ModBands![RegionNames.Wood]);
        Assert.Equal(new PixelRect(0, 0, 81, 46), reading.ModBands[RegionNames.IdleVillagers]);
        Assert.Empty(reading.Clipped);
    }

    [Fact]
    public void Lee_cinco_cifras_de_stock_y_la_poblacion_al_tope()
    {
        var boxes = Boxes(("12", "1250"), ("9", "386"), ("60", "10000"), ("0", "200"), ("150", "200/200"), "3");

        var reading = new HudReader(Fonts()).Read(boxes.GetValueOrDefault, Layout);

        Assert.Equal(10000, reading.Gold);
        Assert.Equal(60, reading.VillagersOn![Resource.Gold]);
        Assert.Equal(150, reading.Villagers);
        Assert.Equal((200, 200), (reading.Population, reading.PopulationCap));
    }

    [Fact]
    public void En_pausa_lee_igual()
    {
        var boxes = Boxes(("12", "1250"), ("9", "386"), ("4", "9999"), ("0", "200"), ("19", "21/30"), "3", darken: 0.2);

        var reading = new HudReader(Fonts()).Read(boxes.GetValueOrDefault, Layout);

        Assert.Equal("bocoach-2026-10", reading.LayoutName);
        Assert.Equal(1250, reading.Wood);
        Assert.Equal(12, reading.VillagersOn![Resource.Wood]);
        Assert.Equal((21, 30), (reading.Population, reading.PopulationCap));
        Assert.Equal(3, reading.IdleVillagers);
    }

    [Fact]
    public void Sin_el_casillero_en_su_lugar_cae_al_camino_viejo()
    {
        var boxes = Boxes(("12", "1250"), ("9", "386"), ("4", "9999"), ("0", "200"), ("19", "21/30"), "3", woodChip: false);

        var reading = new HudReader(Fonts()).Read(boxes.GetValueOrDefault, Layout);

        // La celda de madera, sin su casillero, no se lee por el diseño: no hay aldeanos de madera. Las demás sí.
        Assert.False(reading.VillagersOn!.ContainsKey(Resource.Wood));
        Assert.Equal(386, reading.Food);
        Assert.Equal("bocoach-2026-10", reading.LayoutName);
    }

    [Fact]
    public void Sin_la_letra_hud_numbers_no_usa_el_diseno()
    {
        var boxes = Boxes(("12", "1250"), ("9", "386"), ("4", "9999"), ("0", "200"), ("19", "21/30"), "3");

        var reading = new HudReader(Fonts(withNumbers: false)).Read(boxes.GetValueOrDefault, Layout);

        Assert.Null(reading.LayoutName);
    }
}
