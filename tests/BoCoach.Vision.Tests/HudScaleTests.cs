using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

/// <summary>
/// HUD a otra escala (hoja de ruta B4, parte b): las letras y los íconos están medidos a 1080p con escala 1, así que
/// cada región capturada se reescala por 1/escala antes de leerla y los lectores siguen viendo píxeles de 1080p.
/// </summary>
public class HudScaleTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261018-132830");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    private static readonly string[] Boxed = ["wood", "food", "gold", "stone", "population", "idle_villagers", "clock"];

    private static Dictionary<string, CapturedImage> Boxes() =>
        Boxed.ToDictionary(r => r, r => Png.Read(Path.Combine(Fixture, $"000100_{r}.png")));

    private static string Describe(HudReading r) =>
        $"reloj {r.Clock} · comida {r.Food} · madera {r.Wood} · oro {r.Gold} · piedra {r.Stone} · aldeanos {r.Villagers} "
        + $"· inactivos {r.IdleVillagers} · pob {r.Population}/{r.PopulationCap} · por recurso "
        + string.Join(" ", (r.VillagersOn ?? new Dictionary<Core.Data.Resource, int>()).OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    [Fact]
    public void Resample_da_el_tamano_redondeado()
    {
        var image = new CapturedImage(10, 4, new byte[10 * 4 * 4]);

        var half = image.Resample(0.5);
        var bigger = image.Resample(1.58);

        Assert.Equal((5, 2), (half.Width, half.Height));
        Assert.Equal((16, 6), (bigger.Width, bigger.Height));
        Assert.Same(image, image.Resample(1));
    }

    [Fact]
    public void Achicar_a_la_mitad_promedia_los_pixeles()
    {
        // Una fila: blanco, negro, blanco, negro → a la mitad, cada píxel es el promedio de dos.
        var bgra = new byte[4 * 4];
        for (var x = 0; x < 4; x++)
            bgra[x * 4] = bgra[x * 4 + 1] = bgra[x * 4 + 2] = bgra[x * 4 + 3] = (byte)(x % 2 == 0 ? 255 : 0);
        var image = new CapturedImage(4, 1, bgra);

        var half = image.Resample(0.5);

        Assert.Equal(2, half.Width);
        Assert.All(Enumerable.Range(0, 2), x => Assert.InRange(half.Pixel(x, 0).R, 126, 129));
    }

    [Theory]
    [InlineData(2, 0.5)] // HUD al doble: Scale(2) y de vuelta
    [InlineData(3, 0.5)] // HUD a 1,5: Scale(3), Resample(0,5) y de vuelta por 1/1,5
    public void Agrandada_y_vuelta_a_su_tamano_se_lee_igual(int up, double down)
    {
        var boxes = Boxes();
        var expected = Reader.Read(r => boxes.GetValueOrDefault(r));

        var scale = up * down;
        var scaled = boxes.ToDictionary(b => b.Key, b => b.Value.Scale(up).Resample(down));
        var back = scaled.ToDictionary(b => b.Key, b => b.Value.Resample(1 / scale));
        var actual = Reader.Read(r => back.GetValueOrDefault(r));

        Assert.NotNull(expected.Clock);
        Assert.Equal(Describe(expected), Describe(actual));
    }

    // La cola de producción (íconos de 33 px a 1080p) también: agrandada a 1,09 y 1,5 y vuelta a 1080p se lee igual.
    [Theory]
    [InlineData(330, 1.09)]
    [InlineData(1060, 1.09)]
    [InlineData(1440, 1.09)]
    [InlineData(1060, 1.5)]
    [InlineData(1440, 1.5)]
    public void La_cola_de_produccion_a_otra_escala_se_lee_igual(int frame, double scale)
    {
        var library = IconLibrary.Load(Path.Combine(AppContext.BaseDirectory, "icons", "production_queue.json"));
        var image = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "queue-20261018-021924", $"{frame:D6}_production_queue.png"));

        var back = image.Resample(scale).AtHudScale(scale);

        Assert.Equal(string.Join(" ", library.Read(image)), string.Join(" ", library.Read(back)));
    }

    // Captura real de un amigo (2026-10-20, 1920×1080, HUD a escala 1,09, panel negro de otra civ, comprimida por
    // WhatsApp). Lo que muestra: madera 0 aldeanos y 950, comida 12 y 460, oro 0 y 700, piedra 0 y 800, población 12
    // y 13/15, 0 inactivos, reloj 00:04:15. El "/" de "13/15" sale borroneado por la compresión: se leía "7" hasta que
    // los caracteres que se tocan se separan por el valle de tinta (2026-10-25).
    [Fact]
    public void La_captura_del_amigo_con_el_HUD_mas_grande_se_lee_reescalada()
    {
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "amigo-20261020-1080p-panel-negro.png"));
        var found = AutoCalibration.Detect(screen, RegionLayout.Default)!;

        var reading = Reader.Read(r => found.Layout.Regions.ContainsKey(r)
            ? screen.Crop(found.Layout.PixelsOf(r, screen.Width, screen.Height)).Resample(1 / found.Scale)
            : null);

        Assert.Equal("reloj 255 · comida 460 · madera 950 · oro 700 · piedra 800 · aldeanos 12 · inactivos 0 · pob 13/15 · por recurso Food=12 Wood=0 Stone=0 Gold=0",
            Describe(reading));
    }
}
