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
        // La barra cubre la pantalla entera a cualquier escala del HUD (el juego la recorta en el borde): pedido del
        // usuario en la tercera prueba (2026-10-25), la de 2720 se veía cortada.
        Assert.Equal((4600, 108), (layout.Bar.Width, layout.Bar.Height));
        // Desde la v0.9.67 el log va en el rincón de arriba a la izquierda y la barra empieza a su derecha.
        Assert.Equal(600, layout.Bar.X);
        Assert.Equal(610, layout.Cell("wood").X);
        Assert.Equal(new UnitRect(0, 0, 600, 260), layout.Log);
        // Un solo panel: el fondo de la cola va desde el final de la barra (bar.x, su alto) hasta el final de los íconos
        // con su margen. El marco del juego (BlackPanel) lo dibujan piezas aparte (FramePieces).
        Assert.Equal("BlackPanel", layout.Frame);
        Assert.True(layout.SinglePanel);
        Assert.Equal(new UnitRect(600, 108, 620, 92), layout.QueueBack);
        Assert.Equal(0, layout.CornerRadius);
        // El relleno de las piezas del juego (npanel_*: 24, 21, 18): debajo de cada pieza va un fondo liso opaco de ese
        // color (el relleno translúcido sobre el mapa dañaba la lectura del reloj: prueba de la v0.9.74).
        Assert.Equal((24, 21, 18), layout.Bar.Background.Captured);
        Assert.Equal(new UnitRect(606, 118, 608, 76), layout.Queue);
        Assert.Equal(80, layout.Cell("population").Chip!.Width);
        Assert.Equal(5, layout.Anchors.Count());
        // El reloj va a `gap` del final de la última celda (no de la barra), sobre un campo negro largo que tapa todo
        // su texto ("hh:mm:ss (Normal - 1.7) (Estándar)" se salía del de 230).
        // El campo negro tapa solo la hora (pedido del usuario, 2026-10-31); el resto del texto sigue sobre la barra.
        Assert.Equal(new UnitRect(3334, 12, 240, 84), layout.ClockRect());
    }

    /// <summary>
    /// Prueba de la v0.9.74 (2026-11-01): el Box de un Surround se dibuja desde el origen de su ViewPort y el juego
    /// recorta lo que se sale, así que cada pieza del panel muestra el marco solo en los lados que dan al mapa: por cada
    /// lado que da a otra pieza, el Box se corre una fila de piezas (FrameClip) hacia afuera y esa fila queda recortada.
    /// Debajo de cada pieza va un fondo liso opaco que se mete Seam en las piezas vecinas, para que entre dos fondos no
    /// quede ninguna línea del mapa.
    /// </summary>
    [Fact]
    public void El_marco_del_juego_va_en_seis_piezas_con_el_borde_solo_hacia_el_mapa()
    {
        var layout = Repo();

        var pieces = layout.FramePieces();
        Assert.Equal(["LogTop", "LogBottom", "BarLeft", "BarRight", "Queue", "Clock"], pieces.Select(p => p.Name));
        var byName = pieces.ToDictionary(p => p.Name);
        // El log hasta donde termina la cola: arriba e izquierda; el resto del log: izquierda, abajo y derecha (el escalón).
        Assert.Equal((new UnitRect(0, 0, 600, 200), true, false, true, false), Edges(byName["LogTop"]));
        Assert.Equal((new UnitRect(0, 200, 600, 60), false, true, true, true), Edges(byName["LogBottom"]));
        // La barra sobre la cola: solo arriba; desde el final de la cola hasta el borde: arriba, abajo y derecha.
        Assert.Equal((new UnitRect(600, 0, 620, 108), true, false, false, false), Edges(byName["BarLeft"]));
        Assert.Equal((new UnitRect(1220, 0, 3380, 108), true, true, false, true), Edges(byName["BarRight"]));
        Assert.Equal((new UnitRect(600, 108, 620, 92), false, true, false, true), Edges(byName["Queue"]));
        // El campo del reloj, del alto de la barra: arriba y abajo, donde ya están las líneas de la barra.
        Assert.Equal((new UnitRect(3334, 0, 240, 108), true, true, false, false), Edges(byName["Clock"]));

        // El Box relativo al ViewPort: cada lado interior corrido FrameClip (32, el gridstep de los Surround del juego).
        Assert.Equal(32, HudLayout.FrameClip);
        Assert.Equal(new UnitRect(0, 0, 632, 232), byName["LogTop"].Box);
        Assert.Equal(new UnitRect(0, -32, 600, 92), byName["LogBottom"].Box);
        Assert.Equal(new UnitRect(-32, 0, 684, 140), byName["BarLeft"].Box);
        Assert.Equal(new UnitRect(-32, 0, 3412, 108), byName["BarRight"].Box);
        Assert.Equal(new UnitRect(-32, -32, 652, 124), byName["Queue"].Box);
        Assert.Equal(new UnitRect(-32, 0, 304, 108), byName["Clock"].Box);
        // El fondo opaco: la pieza más Seam (4) hacia cada vecina.
        Assert.Equal(4, HudLayout.Seam);
        Assert.Equal(new UnitRect(0, 0, 604, 204), byName["LogTop"].Base);
        Assert.Equal(new UnitRect(0, 196, 600, 64), byName["LogBottom"].Base);
        Assert.Equal(new UnitRect(596, 0, 628, 112), byName["BarLeft"].Base);
        Assert.Equal(new UnitRect(1216, 0, 3384, 108), byName["BarRight"].Base);
        Assert.Equal(new UnitRect(596, 104, 624, 96), byName["Queue"].Base);
        Assert.Equal(new UnitRect(3330, 0, 248, 108), byName["Clock"].Base);

        static (UnitRect, bool Top, bool Bottom, bool Left, bool Right) Edges(FramePiece p) => (p.Rect, p.Top, p.Bottom, p.Left, p.Right);
    }

    private static HudLayout Minimal() => HudLayout.Load(Path.Combine(AppContext.BaseDirectory, HudLayout.MinimalFileName));

    // Diseño mínimo (2026-11-01): la barra del juego opaca de borde a borde, sin log ni cola corridos, con la franja fina
    // de color bajo cada ícono y el hueco de la GD a la derecha de los aldeanos.
    [Fact]
    public void El_diseno_minimo_es_la_barra_sola_con_el_marco_y_la_guia_junto_a_los_aldeanos()
    {
        var layout = Minimal();

        Assert.Equal("bocoach-minimo-2026-11", layout.Name);
        Assert.Equal(0, layout.Bar.X);
        Assert.Null(layout.Log);
        Assert.Null(layout.Queue);
        Assert.Null(layout.MenuScale);
        Assert.False(layout.SinglePanel);
        Assert.Equal("BlackPanel", layout.Frame);
        var pieces = layout.FramePieces();
        Assert.Equal(["Bar", "Clock"], pieces.Select(p => p.Name));
        var bar = pieces[0];
        Assert.Equal((new UnitRect(0, 0, 4600, 108), true, true, true, true), (bar.Rect, bar.Top, bar.Bottom, bar.Left, bar.Right));
        Assert.Equal(new UnitRect(0, 0, 4600, 108), bar.Box);
        Assert.Equal(new UnitRect(0, 0, 4600, 108), bar.Base);
        Assert.Equal(new UnitRect(2734, 0, 240, 108), pieces[1].Rect);

        var wood = layout.Cell("wood");
        Assert.Equal(10, wood.X);
        Assert.Equal(new HudChip(10, 96, 84, 10, new HudColor(0.53, 0.36, 0.16)), wood.Chip);
        Assert.Equal(28, wood.Fields["villagers"].Points);
        Assert.Equal(48, wood.Fields["stock"].Points);
        Assert.True(wood.Fields["stock"].AlignLeft);
        Assert.True(wood.Fields["villagers"].AlignLeft);
        Assert.False(layout.Cell("age").Fields["text"].AlignLeft);
        Assert.False(Repo().Cell("wood").Fields["stock"].AlignLeft);
        Assert.Equal(new UnitRect(220, 64, 184, 40), wood.Guide);
        Assert.Empty(wood.Backgrounds);
        Assert.Null(layout.Cell("idle").Guide);
        Assert.Equal(5, layout.Anchors.Count());
    }

    [Fact]
    public void La_guia_va_en_proporcion_dentro_de_la_caja_y_sobrevive_la_ida_y_vuelta()
    {
        var layout = Minimal();
        var wood = layout.Cell("wood");

        // Caja de la celda a 0,4348 px por unidad (178×47): la guía arranca a 220/410 del ancho y 64/108 del alto.
        var again = HudLayout.Parse(layout.ToJson());
        Assert.Equal(wood.Guide, again.Cell("wood").Guide);
        Assert.True(again.Cell("wood").Fields["stock"].AlignLeft);
        Assert.Equal(new BoCoach.Vision.Capture.PixelRect(96, 28, 80, 17), layout.GuideRect(wood, new BoCoach.Vision.Capture.PixelRect(0, 0, 178, 47)));
    }

    [Fact]
    public void El_marco_pide_el_log_y_la_cola_en_un_solo_panel()
    {
        var json = Repo().ToJson().Replace("\"queue\":", "\"cola\":");

        var error = Assert.Throws<InvalidDataException>(() => HudLayout.Parse(json));

        Assert.Contains("frame", error.Message);
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
