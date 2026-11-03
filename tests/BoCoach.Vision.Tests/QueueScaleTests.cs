using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

/// <summary>
/// La cola de producción a otra resolución o escala del HUD (camino al público, `docs/superpowers/specs/2026-11-02-cola-y-carga-a-otra-escala-design.md`).
/// Los íconos siguen la escala del HUD: la región se lleva a 1080p antes de leerla (<see cref="CapturedImage.AtHudScale"/>)
/// y tiene que leer lo mismo que el fixture a 1080p, aunque la escala medida esté un poco corrida.
/// </summary>
public class QueueScaleTests
{
    private static readonly IconLibrary Library = IconLibrary.Load(Path.Combine(AppContext.BaseDirectory, "icons", "production_queue.json"));

    /// <summary>Todos los fixtures de la cola con lo que leen a 1080p (lo mismo que fija <see cref="ProductionQueueTests"/>).</summary>
    public static TheoryData<string, int, string> Fixtures()
    {
        var data = new TheoryData<string, int, string>();
        (string Recording, int Frame, string Expected)[] cases =
        [
            ("20261018-132830", 32, ""),
            ("20261018-132830", 280, "villager loom…"),
            ("20261018-132830", 690, "villager feudal_age…"),
            ("20261018-132830", 946, "villager archer archer"),
            ("20261101-230201", 718, "villager militia"),
            ("20261101-230201", 734, "villager militia"),
            ("20261101-230201", 758, "villager militia"),
            ("20261101-230201", 762, "villager housed"),
            ("20261101-230201", 1172, "archer"),
            ("20261029-211516", 1064, "villager archer archer"),
            ("20261029-211516", 1558, "villager archer archer wheelbarrow…"),
            ("20261029-211516", 1812, "villager archer archer castle_age…"),
            ("20261018-021924", 330, "loom villager…"),
            ("20261018-021924", 700, "feudal_age"),
            ("20261018-021924", 1060, "villager double_bit_axe horse_collar archer archer"),
            ("20261018-021924", 1440, "villager archer archer"),
            ("20261018-021924", 1510, "wheelbarrow"),
            ("20261018-021924", 1578, ""),
        ];
        foreach (var (recording, frame, expected) in cases)
            data.Add(recording, frame, expected);
        return data;
    }

    private static CapturedImage Frame(string recording, int frame) =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", $"queue-{recording}", $"{frame:D6}_production_queue.png"));

    private static string Read(CapturedImage image) => string.Join(" ", Library.Read(image));

    /// <summary>A 1440p, 2160p y 1600×900 (escala 1,333, 2 y 0,9): la caja capturada a esa escala y llevada a 1080p lee lo mismo.</summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_otra_resolucion_llevada_a_1080p_lee_lo_mismo(string recording, int frame, string expected)
    {
        var image = Frame(recording, frame);
        Assert.Equal(expected, Read(image));
        foreach (var scale in new[] { 1440 / 1080.0, 2160 / 1080.0, 900 / 1080.0 })
            Assert.Equal(expected, Read(image.Resample(scale).AtHudScale(scale)));
    }

    /// <summary>
    /// La escala medida no es exacta (con las franjas del mod viejo, ±1,4 %): los íconos quedan de 32,5 o 33,5 px después
    /// de reescalar, el último lugar se corre hasta 6 px, y el lector tiene que seguir el paso real.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Con_la_escala_medida_corrida_sigue_el_paso_real(string recording, int frame, string expected)
    {
        var image = Frame(recording, frame);
        foreach (var scale in new[] { 1440 / 1080.0, 2160 / 1080.0, 900 / 1080.0 })
        {
            foreach (var error in new[] { 1.015, 0.985 })
                Assert.Equal(expected, Read(image.Resample(scale).AtHudScale(scale * error)));
        }
    }

    /// <summary>A escala 1 cada ícono se encuentra donde siempre (k × 33), sin corrimiento: las lecturas de siempre no cambian.</summary>
    [Theory]
    [InlineData("20261018-021924", 1060)]
    [InlineData("20261018-132830", 946)]
    public void A_escala_1_los_iconos_caen_cada_33_px(string recording, int frame)
    {
        var slots = Library.Locate(Frame(recording, frame));

        Assert.NotEmpty(slots);
        Assert.Equal(Enumerable.Range(0, slots.Count).Select(k => k * ProductionQueue.IconSize), slots.Select(s => s.X));
    }
}
