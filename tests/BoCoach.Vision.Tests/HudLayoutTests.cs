using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// El diseño del panel de recursos del mod (data/hud_layout.json): de él salen el mod, la calibración sola, el lector
/// y el lugar de la GD. Acá, que cargue, que cierre (nada se solapa ni se sale) y que convierta unidades a píxeles.
/// </summary>
public class HudLayoutTests
{
    private static HudLayout Repo() => HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));

    [Fact]
    public void Carga_el_diseno_del_repo_con_sus_siete_celdas_en_orden()
    {
        var layout = Repo();

        Assert.Equal("bocoach-2026-10", layout.Name);
        Assert.Equal(0.4286, layout.PixelsPerUnitAt1080p, 4);
        Assert.Equal(HudLayout.CellOrder, layout.Cells.Select(c => c.Name));
        Assert.Equal((2720, 108), (layout.Bar.Width, layout.Bar.Height));
        Assert.Equal(10, layout.Cell("wood").X);
        Assert.Equal(80, layout.Cell("population").Chip!.Width);
        Assert.Equal(5, layout.Anchors.Count());
        Assert.Equal(new UnitRect(2734, 12, 230, 84), layout.ClockRect());
    }

    [Fact]
    public void Las_celdas_no_se_solapan_y_todo_entra_en_su_celda()
    {
        var layout = Repo();
        HudCell? previous = null;
        foreach (var cell in layout.Cells)
        {
            if (previous is not null)
                Assert.True(cell.X >= previous.X + previous.Width, $"{cell.Name} pisa a {previous.Name}");
            Assert.True(cell.X + cell.Width <= layout.Bar.Width, $"{cell.Name} se sale de la barra");
            var inside = new List<(string What, int X, int Y, int W, int H)>
            {
                ("ícono", cell.Icon.X, cell.Icon.Y, cell.Icon.Size, cell.Icon.Size),
            };
            if (cell.Chip is { } chip)
                inside.Add(("casillero", chip.X, chip.Y, chip.Width, chip.Height));
            inside.AddRange(cell.Fields.Select(f => (f.Key, f.Value.X, f.Value.Y, f.Value.Width, f.Value.Height)));
            inside.AddRange(cell.Backgrounds.Select((b, i) => ($"fondo {i}", b.X, b.Y, b.Width, b.Height)));
            if (cell.Separator is { } separator)
                inside.Add(("separador", separator.X, separator.Y, separator.Width, separator.Height));
            foreach (var (what, x, y, w, h) in inside)
                Assert.True(x >= 0 && y >= 0 && x + w <= cell.Width && y + h <= layout.Bar.Height, $"{what} de {cell.Name} se sale de la celda");
            previous = cell;
        }
    }

    [Fact]
    public void El_rectangulo_de_un_campo_dentro_de_su_caja_va_en_proporcion()
    {
        var layout = Repo();
        var box = new PixelRect(100, 20, 176, 46); // la celda de madera (410 × 108 unidades) a 0,4286 px por unidad

        Assert.Equal(new PixelRect(218, 25, 57, 36), layout.FieldRect(layout.Cell("wood"), "stock", box));
        Assert.Equal(new PixelRect(145, 25, 30, 36), layout.ChipRect(layout.Cell("wood"), box));
    }

    [Fact]
    public void Un_diseno_sin_un_campo_dice_cual_falta()
    {
        var json = Repo().ToJson().Replace("\"stock\"", "\"stok\"");

        var error = Assert.Throws<InvalidDataException>(() => HudLayout.Parse(json));

        Assert.Contains("stock", error.Message);
    }

    [Fact]
    public void Ida_y_vuelta_por_JSON()
    {
        var json = Repo().ToJson();

        Assert.Equal(json, HudLayout.Parse(json).ToJson());
    }

    [Fact]
    public void Regiones_y_celdas_se_corresponden()
    {
        Assert.Equal(RegionNames.IdleVillagers, HudLayout.RegionOf("idle"));
        Assert.Equal("population", HudLayout.CellOf(RegionNames.Population));
        Assert.Null(HudLayout.CellOf(RegionNames.Clock));
    }
}
