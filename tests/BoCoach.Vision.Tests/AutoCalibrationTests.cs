using System.Text.Json;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Calibración sola (hoja de ruta B4): con el mod, los cuadrados negros con franja de color y el rectángulo negro del
/// reloj son anclas que se encuentran en cualquier resolución. No hay capturas de pantalla entera, así que se arma una:
/// fondo liso y cada caja de la grabación 20261018-132830 pegada donde la calibración manual la tenía (session.json).
/// </summary>
public class AutoCalibrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261018-132830");

    private static readonly string[] Anchored = [RegionNames.Wood, RegionNames.Food, RegionNames.Gold, RegionNames.Stone, RegionNames.Population, RegionNames.IdleVillagers, RegionNames.Clock];

    /// <summary>Las cajas en píxeles de la calibración con que se grabó.</summary>
    private static Dictionary<string, PixelRect> RecordedBoxes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture, "session.json")));
        return doc.RootElement.GetProperty("regions").EnumerateObject().ToDictionary(
            r => r.Name,
            r => { var p = r.Value.GetProperty("pixels").EnumerateArray().Select(v => v.GetInt32()).ToArray(); return new PixelRect(p[0], p[1], p[2], p[3]); });
    }

    /// <summary>Una pantalla de 1920×1080 con el fondo del panel y las cajas grabadas (frame 100) en su lugar.</summary>
    internal static CapturedImage Composite(Dictionary<string, PixelRect> boxes)
    {
        const int w = 1920, h = 1080;
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < bgra.Length; i += 4)
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (52, 58, 70, 255); // marrón del panel, nunca negro puro
        foreach (var (name, box) in boxes)
        {
            var path = Path.Combine(Fixture, $"000100_{name}.png");
            if (!File.Exists(path))
                continue;
            var image = Png.Read(path);
            for (var y = 0; y < image.Height; y++)
                Buffer.BlockCopy(image.Bgra, y * image.Width * 4, bgra, ((box.Y + y) * w + box.X) * 4, image.Width * 4);
        }
        return new CapturedImage(w, h, bgra);
    }

    private static void AssertClose(PixelRect expected, PixelRect actual, int tolerance, string what)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance
            && Math.Abs(expected.X + expected.Width - actual.X - actual.Width) <= tolerance
            && Math.Abs(expected.Y + expected.Height - actual.Y - actual.Height) <= tolerance,
            $"{what}: se esperaba {expected} y se encontró {actual}");
    }

    [Fact]
    public void Encuentra_los_seis_cuadrados_y_el_reloj_y_arma_las_mismas_cajas_que_la_calibracion_manual()
    {
        var boxes = RecordedBoxes();
        var screen = Composite(boxes);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        output.WriteLine($"escala {found.Scale:0.000} · cuadrados {string.Join(" ", found.Squares)} · reloj {found.Clock}");
        Assert.Equal(6, found.Squares.Count);
        Assert.Equal(1.0, found.Scale, 0.05);
        foreach (var name in Anchored)
        {
            var actual = found.Layout.PixelsOf(name, screen.Width, screen.Height);
            output.WriteLine($"{name,-15} manual {boxes[name]}  sola {actual}");
            AssertClose(boxes[name], actual, 3, name);
        }
        // Las demás regiones también están (el log, la edad, la cola), aunque no tengan ancla.
        Assert.All(RegionNames.All, name => Assert.False(found.Layout.PixelsOf(name, screen.Width, screen.Height).IsEmpty));
    }

    [Fact]
    public void A_doble_resolucion_encuentra_lo_mismo_al_doble()
    {
        var boxes = RecordedBoxes();
        var screen = Composite(boxes).Scale(2);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        Assert.Equal(2.0, found.Scale, 0.1);
        foreach (var name in Anchored)
        {
            var box = boxes[name];
            var expected = new PixelRect(box.X * 2, box.Y * 2, box.Width * 2, box.Height * 2);
            AssertClose(expected, found.Layout.PixelsOf(name, screen.Width, screen.Height), 6, name);
        }
    }

    // Captura real de un amigo (2026-10-20, 1920×1080, juego en castellano, HUD un 10 % más grande y panel negro, el
    // de otra civ): las franjas de color siguen ahí aunque el negro de los cuadrados se confunda con el panel.
    [Fact]
    public void Con_el_panel_negro_de_otra_civ_y_el_HUD_mas_grande_encuentra_las_franjas_y_el_reloj()
    {
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "amigo-20261020-1080p-panel-negro.png"));

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        output.WriteLine($"escala {found.Scale:0.000} · cuadrados {string.Join(" ", found.Squares)}");
        foreach (var name in RegionNames.All)
            output.WriteLine($"{name,-17} {found.Layout.PixelsOf(name, screen.Width, screen.Height)}");
        Assert.Equal(6, found.Squares.Count);
        Assert.All(found.Squares, s => Assert.InRange(s.Width, 37, 42));
        Assert.InRange(found.Scale, 1.0, 1.2);
        Assert.Equal(found.Scale, found.Layout.HudScale); // queda en regions.json para reescalar las capturas
        var wood = found.Layout.PixelsOf(RegionNames.Wood, screen.Width, screen.Height);
        Assert.InRange(wood.X, 4, 9);
        Assert.InRange(wood.Y, 4, 8);
        var clock = found.Layout.PixelsOf(RegionNames.Clock, screen.Width, screen.Height);
        Assert.InRange(clock.X, 840, 856); // "00:04:15" empieza a los 849 px
        var age = found.Layout.PixelsOf(RegionNames.Age, screen.Width, screen.Height);
        Assert.InRange(age.X, 640, 665); // "Alta Edad Media" empieza a los 656 px (el medallón de la edad, antes, no cuenta)
        Assert.True(age.X + age.Width < clock.X);
    }

    [Fact]
    public void Sin_el_mod_no_calibra()
    {
        var boxes = RecordedBoxes();
        // El reloj solo (un rectángulo negro) no alcanza: hacen falta los cuadrados con franja.
        var screen = Composite(boxes.Where(b => b.Key == RegionNames.Clock).ToDictionary());

        Assert.Null(AutoCalibration.Detect(screen, RegionLayout.Default));
    }

    // ---- Con el diseño (data/hud_layout.json): las anclas son los cinco casilleros de color ----

    private static readonly HudLayout Layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));

    private static readonly Dictionary<string, PixelRect> LayoutBoxes = new()
    {
        [RegionNames.Wood] = new(6 + 4, 4, 176, 46),
        [RegionNames.Population] = new(6 + 707, 4, 163, 46),
        [RegionNames.IdleVillagers] = new(6 + 870, 4, 81, 46),
        [RegionNames.Age] = new(6 + 996, 4 + 12, 162, 22),
        [RegionNames.Clock] = new(6 + 1172, 4 + 5, 99, 36),
    };

    [Fact]
    public void Con_el_diseno_encuentra_los_cinco_casilleros_y_arma_las_cajas_desde_el_diseno()
    {
        var screen = HudLayoutScreens.Render(Layout, 0.4286, originX: 6, originY: 4);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Equal("bocoach-2026-10", found.Hud!.Name);
        Assert.Equal(5, found.Squares.Count);
        Assert.Equal(1.0, found.Scale, 0.03);
        foreach (var (name, expected) in LayoutBoxes)
        {
            var actual = found.Layout.PixelsOf(name, screen.Width, screen.Height);
            output.WriteLine($"{name,-15} esperado {expected}  sola {actual}");
            AssertClose(expected, actual, name == RegionNames.Clock ? 3 : 2, name);
        }
        Assert.Equal("bocoach-2026-10", found.Layout.Hud!.Name);
        Assert.Equal(found.Scale, found.Layout.HudScale);
    }

    [Fact]
    public void A_escala_1_5_lo_mismo_al_1_5()
    {
        var screen = HudLayoutScreens.Render(Layout, 0.6429, 2880, 1620, originX: 9, originY: 6);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Equal(1.5, found.Scale, 0.05);
        foreach (var (name, box) in LayoutBoxes)
        {
            var expected = new PixelRect((int)Math.Round(box.X * 1.5), (int)Math.Round(box.Y * 1.5), (int)Math.Round(box.Width * 1.5), (int)Math.Round(box.Height * 1.5));
            AssertClose(expected, found.Layout.PixelsOf(name, screen.Width, screen.Height), 3, name);
        }
    }

    [Fact]
    public void Con_la_GD_dibujada_adentro_de_los_casilleros_encuentra_lo_mismo()
    {
        var screen = HudLayoutScreens.Render(Layout, 0.4286, originX: 6, originY: 4, gdInside: true);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Equal(5, found.Squares.Count);
        foreach (var (name, expected) in LayoutBoxes)
            AssertClose(expected, found.Layout.PixelsOf(name, screen.Width, screen.Height), name == RegionNames.Clock ? 3 : 2, name);
    }

    [Fact]
    public void Sin_los_casilleros_del_diseno_cae_a_la_deteccion_del_mod_viejo()
    {
        var boxes = RecordedBoxes();
        var screen = Composite(boxes);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Null(found.Hud);
        Assert.Null(found.Layout.Hud);
        Assert.Equal(6, found.Squares.Count);
        foreach (var name in Anchored)
            AssertClose(boxes[name], found.Layout.PixelsOf(name, screen.Width, screen.Height), 3, name);
    }

    /// <summary>
    /// La primera captura real del panel nuevo (el usuario, 2026-10-24, 1920×1080, recortada a la franja del HUD sobre
    /// fondo verde): los casilleros caen a 178 px cada 410 unidades, o sea 0,4348 px por unidad, un 1,5 % más que el
    /// 0,4286 del diseño. La escala se mide por las posiciones, no por el ancho de 30 px de los casilleros, que no
    /// distingue una de otra. El "5" de aldeanos parte el casillero azul en dos y el aviso amarillo del overlay quedó
    /// abajo de la barra: nada de eso molesta.
    /// </summary>
    [Fact]
    public void Con_la_captura_real_del_usuario_calibra_con_la_escala_real()
    {
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "hud-layout-20261024-1080p.png"));

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Equal("bocoach-2026-10", found.Hud!.Name);
        Assert.Equal(5, found.Squares.Count);
        Assert.Equal(1.0145, found.Scale, 0.01);
        AssertClose(new PixelRect(50, 5, 30, 37), found.Squares[0], 1, "casillero de madera");
        AssertClose(new PixelRect(763, 5, 35, 37), found.Squares[4], 1, "casillero de población");
        const double ppu = 0.4348;
        foreach (var cell in Layout.Cells)
        {
            var rect = cell.Name == "age" ? Layout.AgeTextRect() : Layout.CellRect(cell);
            var expected = new PixelRect((int)Math.Round(rect.X * ppu), (int)Math.Round(rect.Y * ppu), (int)Math.Round(rect.Width * ppu), (int)Math.Round(rect.Height * ppu));
            var actual = found.Layout.PixelsOf(HudLayout.RegionOf(cell.Name), screen.Width, screen.Height);
            output.WriteLine($"{cell.Name,-12} esperado {expected}  sola {actual}");
            AssertClose(expected, actual, 2, cell.Name);
        }
    }

    [Fact]
    public void Con_el_HUD_un_poco_mas_grande_que_el_diseno_calibra_igual()
    {
        const double ppu = 0.4348;
        var screen = HudLayoutScreens.Render(Layout, ppu);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.Equal(ppu / Layout.PixelsPerUnitAt1080p, found.Scale, 0.01);
        foreach (var cell in Layout.Cells)
        {
            var rect = cell.Name == "age" ? Layout.AgeTextRect() : Layout.CellRect(cell);
            var expected = new PixelRect((int)Math.Round(rect.X * ppu), (int)Math.Round(rect.Y * ppu), (int)Math.Round(rect.Width * ppu), (int)Math.Round(rect.Height * ppu));
            AssertClose(expected, found.Layout.PixelsOf(HudLayout.RegionOf(cell.Name), screen.Width, screen.Height), 2, cell.Name);
        }
    }

    /// <summary>
    /// El reloj está en otro panel del juego y puede no caer donde el diseño lo predice: en la segunda prueba del
    /// 2026-10-24 cayó 178 px a la derecha (el origen de su colección se mide en unidades de pantalla, no del HUD).
    /// Su rectángulo negro se busca en toda la franja a la derecha de la barra, no solo a 40 px.
    /// </summary>
    [Fact]
    public void Con_el_reloj_lejos_de_donde_el_diseno_lo_predice_lo_encuentra_igual()
    {
        const double ppu = 0.4348;
        const int shift = 178;
        var screen = HudLayoutScreens.Render(Layout, ppu, clockShift: shift);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        var clock = Layout.ClockRect();
        var expected = new PixelRect((int)Math.Round(clock.X * ppu) + shift, (int)Math.Round(clock.Y * ppu), (int)Math.Round(clock.Width * ppu), (int)Math.Round(clock.Height * ppu));
        AssertClose(expected, found.Clock, 2, "clock");
    }

    /// <summary>
    /// La cola de producción no es parte del diseño: su caja salía de la calibración de referencia escalada, y la
    /// referencia era la calibración sola anterior, ya escalada, así que cada recalibración (cada 5 s sin reloj) la
    /// agrandaba un 1,5 %: en la tercera prueba del 2026-10-25 quedó en y 121 con la cola real en y 50, y el centro
    /// urbano parecía parado toda la partida. Sale del lugar de siempre a 1080p escalado, como el panel de puntajes.
    /// </summary>
    [Fact]
    public void La_caja_de_la_cola_no_se_agranda_al_volver_a_calibrar()
    {
        const double ppu = 0.4348;
        var screen = HudLayoutScreens.Render(Layout, ppu);
        var regions = RegionLayout.Default.Regions.ToDictionary(kv => kv.Key, kv => kv.Value);
        regions[RegionNames.ProductionQueue] = new RegionRect(0, 0.112, 0.616, 0.119);
        var reference = new RegionLayout(1920, 1080, regions, 1.0146, null);

        var found = AutoCalibration.Detect(screen, reference, Layout);

        Assert.NotNull(found);
        var scale = ppu / Layout.PixelsPerUnitAt1080p;
        var usual = RegionLayout.Default.PixelsOf(RegionNames.ProductionQueue, 1920, 1080);
        var expected = new PixelRect((int)Math.Round(usual.X * scale), (int)Math.Round(usual.Y * scale), (int)Math.Round(usual.Width * scale), (int)Math.Round(usual.Height * scale));
        AssertClose(expected, found.Layout.PixelsOf(RegionNames.ProductionQueue, 1920, 1080), 2, "cola");
        var again = AutoCalibration.Detect(screen, found.Layout, Layout);
        Assert.NotNull(again);
        Assert.Equal(found.Layout.PixelsOf(RegionNames.ProductionQueue, 1920, 1080), again.Layout.PixelsOf(RegionNames.ProductionQueue, 1920, 1080));
    }

    /// <summary>
    /// El aviso de inactivos del overlay es un marco rojo alrededor de la celda de inactivos, de un rojo a menos de 45
    /// por canal del casillero de comida y más ancho que él: no puede ganarle el lugar de ancla (su alto no es el de
    /// un casillero).
    /// </summary>
    [Fact]
    public void Con_el_marco_de_inactivos_del_overlay_a_la_vista_calibra_igual()
    {
        var screen = HudLayoutScreens.Render(Layout, 0.4286, originX: 6, originY: 4, gdInside: true, idleFrame: true);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, Layout);

        Assert.NotNull(found);
        Assert.NotNull(found.Hud);
        foreach (var (name, expected) in LayoutBoxes)
            AssertClose(expected, found.Layout.PixelsOf(name, screen.Width, screen.Height), name == RegionNames.Clock ? 3 : 2, name);
    }

    [Fact]
    public void Un_casillero_corrido_no_calibra()
    {
        var screen = HudLayoutScreens.Render(Layout, 0.4286, originX: 6, originY: 4);
        // El casillero de piedra pintado 12 px más a la derecha (el original, tapado con el gris de la barra).
        var stone = Layout.Cell("stone");
        var chip = Layout.ChipRect(stone, new PixelRect(6 + (int)Math.Round(stone.X * 0.4286), 4, 176, 46));
        var color = stone.Chip!.Color.Captured;
        for (var y = chip.Y; y < chip.Y + chip.Height; y++)
        {
            for (var x = chip.X; x < chip.X + chip.Width + 12; x++)
            {
                var i = (y * screen.Width + x) * 4;
                var moved = x >= chip.X + 12;
                (screen.Bgra[i], screen.Bgra[i + 1], screen.Bgra[i + 2]) = moved ? ((byte)color.B, (byte)color.G, (byte)color.R) : ((byte)26, (byte)26, (byte)26);
            }
        }

        Assert.Null(AutoCalibration.Detect(screen, RegionLayout.Default, Layout));
    }
}
