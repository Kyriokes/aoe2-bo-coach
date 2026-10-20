using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// HUD a otra escala (hoja de ruta B4, parte b): las letras y los íconos están medidos a 1080p con escala 1, así que
/// cada región capturada se reescala por 1/escala antes de leerla y los lectores siguen viendo píxeles de 1080p.
/// </summary>
public class HudScaleTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "hud-20261018-132830");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    private static readonly string[] Regions = ["wood", "food", "gold", "stone", "population", "idle_villagers", "clock"];

    private static Dictionary<string, CapturedImage> Boxes() =>
        Regions.ToDictionary(r => r, r => Png.Read(Path.Combine(Fixture, $"000100_{r}.png")));

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
}
