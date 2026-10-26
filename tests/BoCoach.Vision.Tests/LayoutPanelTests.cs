using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Con el diseño, cada caja grabada es una celda entera: el casillero se reconoce por su color en el lugar que dice
/// el diseño y cada campo se recorta por proporción, afinado a los bordes de su fondo negro.
/// </summary>
public class LayoutPanelTests
{
    private static readonly HudLayout Layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));
    private static readonly HudCell Wood = Layout.Cell("wood");
    private const double PixelsPerUnit = 0.4286;

    /// <summary>
    /// La celda de madera como la graba el recorder a 1080p (176×46): fondo gris de la barra, casillero de su color en
    /// su lugar, fondo negro del campo corrido <paramref name="shiftBlack"/> px, y con <paramref name="gdInside"/> la GD
    /// de la app (un rectángulo negro en el medio del casillero dejando 2 px de borde).
    /// </summary>
    private static CapturedImage WoodBox(int shiftBlack = 0, bool chip = true, bool gdInside = false)
    {
        var (w, h) = ((int)Math.Round(Wood.Width * PixelsPerUnit), (int)Math.Round(Layout.Bar.Height * PixelsPerUnit));
        var bgra = new byte[w * h * 4];
        void Fill(PixelRect r, (int R, int G, int B) c)
        {
            for (var y = Math.Max(0, r.Y); y < Math.Min(h, r.Y + r.Height); y++)
                for (var x = Math.Max(0, r.X); x < Math.Min(w, r.X + r.Width); x++)
                {
                    var i = (y * w + x) * 4;
                    (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = ((byte)c.B, (byte)c.G, (byte)c.R, 255);
                }
        }
        var box = new PixelRect(0, 0, w, h);
        Fill(box, (26, 26, 26));
        var chipRect = Layout.ChipRect(Wood, box);
        if (chip)
            Fill(chipRect, Wood.Chip!.Color.Captured);
        if (gdInside)
            Fill(new PixelRect(chipRect.X + 2, chipRect.Y + 2, chipRect.Width - 4, chipRect.Height - 4), (0, 0, 0));
        var back = Wood.Backgrounds[0];
        var backRect = new PixelRect(
            (int)Math.Round(back.X * PixelsPerUnit) + shiftBlack, (int)Math.Round(back.Y * PixelsPerUnit),
            (int)Math.Round(back.Width * PixelsPerUnit), (int)Math.Round(back.Height * PixelsPerUnit));
        Fill(backRect, (0, 0, 0));
        return new CapturedImage(w, h, bgra);
    }

    [Fact]
    public void Encuentra_el_casillero_por_su_color_aunque_la_GD_tape_el_medio()
    {
        var box = WoodBox(gdInside: true);
        var expected = Layout.ChipRect(Wood, new PixelRect(0, 0, box.Width, box.Height));

        Assert.Equal(expected, LayoutPanel.Chip(Layout, Wood, box));
        Assert.Null(LayoutPanel.Chip(Layout, Wood, WoodBox(chip: false)));
    }

    /// <summary>
    /// En población el total va escrito sobre el casillero: un número de tres cifras, o el suavizado de los bordes
    /// del juego, puede tocar las líneas que se miran a 1 px del borde. Unos píxeles sueltos no lo rechazan; un
    /// tercio de la línea de otro color (casillero corrido), sí.
    /// </summary>
    [Fact]
    public void Un_numero_que_toca_la_linea_del_borde_no_pierde_el_casillero()
    {
        var box = WoodBox();
        var chip = Layout.ChipRect(Wood, new PixelRect(0, 0, box.Width, box.Height));
        void White(int x, int y) { var i = (y * box.Width + x) * 4; (box.Bgra[i], box.Bgra[i + 1], box.Bgra[i + 2]) = (255, 255, 255); }
        for (var x = chip.X + chip.Width / 2 - 1; x <= chip.X + chip.Width / 2 + 1; x++)
            White(x, chip.Y + 1);
        for (var y = chip.Y + chip.Height / 2 - 1; y <= chip.Y + chip.Height / 2 + 1; y++)
            White(chip.X + chip.Width - 2, y);

        Assert.Equal(chip, LayoutPanel.Chip(Layout, Wood, box));

        for (var x = chip.X + 1; x < chip.X + 1 + chip.Width / 3; x++)
            White(x, chip.Y + 1);
        Assert.Null(LayoutPanel.Chip(Layout, Wood, box));
    }

    [Fact]
    public void La_celda_de_inactivos_devuelve_la_caja_entera()
    {
        var box = WoodBox();

        Assert.Equal(new PixelRect(0, 0, box.Width, box.Height), LayoutPanel.Chip(Layout, Layout.Cell("idle"), box));
    }

    [Fact]
    public void El_campo_se_afina_a_los_bordes_del_negro()
    {
        var exact = WoodBox();
        var proportional = Layout.FieldRect(Wood, "stock", new PixelRect(0, 0, exact.Width, exact.Height));

        Assert.Equal(proportional, LayoutPanel.Field(Layout, Wood, "stock", exact));
        // El fondo negro 2 px más a la derecha: el campo se corre con él.
        Assert.Equal(proportional.X + 2, LayoutPanel.Field(Layout, Wood, "stock", WoodBox(shiftBlack: 2)).X);
        // Corrido 6 px, se afina solo hasta la tolerancia de 3.
        Assert.Equal(proportional.X + 3, LayoutPanel.Field(Layout, Wood, "stock", WoodBox(shiftBlack: 6)).X);
    }
}
