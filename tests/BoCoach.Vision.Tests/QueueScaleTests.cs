using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;

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

    // De punta a punta, con la captura real del usuario (HUD propio, 1:57 de partida, un aldeano en la cola): en otra
    // resolución la calibración sola mide la escala de la pantalla, ubica la caja de la cola y el aldeano se lee.
    [Theory]
    [InlineData(1080)]
    [InlineData(900)]
    [InlineData(1440)]
    [InlineData(2160)]
    public void Con_la_pantalla_real_calibra_ubica_la_caja_y_lee(int height)
    {
        var layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "hud_layout-2026-10.json"));
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "hud-layout-20261024-1080p.png")).Resample(height / 1080.0);

        var found = AutoCalibration.Detect(screen, RegionLayout.Default, layout);

        Assert.NotNull(found);
        Assert.Equal(height / 1080.0, found.Scale, 0.005);
        var box = found.Layout.PixelsOf(RegionNames.ProductionQueue, screen.Width, screen.Height);
        Assert.Equal("villager", Assert.Single(Library.Read(screen.Crop(box).AtHudScale(found.Scale))).Key);

        // El panel de puntajes queda anclado abajo a la derecha y crece con la escala, a cualquier resolución.
        var scores = RegionLayout.Default.PixelsOf(RegionNames.Scores, 1920, 1080);
        var k = height / 1080.0;
        var expected = new PixelRect(screen.Width - (int)Math.Round((1920 - scores.X) * k), screen.Height - (int)Math.Round((1080 - scores.Y) * k), (int)Math.Round(scores.Width * k), (int)Math.Round(scores.Height * k));
        var actual = found.Layout.PixelsOf(RegionNames.Scores, screen.Width, screen.Height);
        Assert.InRange(actual.X, expected.X - 3, expected.X + 3);
        Assert.InRange(actual.Y, expected.Y - 3, expected.Y + 3);
        Assert.InRange(actual.Width, expected.Width - 3, expected.Width + 3);
        Assert.InRange(actual.Height, expected.Height - 3, expected.Height + 3);
    }

    // Captura real de un amigo (escala 1,09, comprimida por WhatsApp): un aldeano casi listo con "10" en cola. El número
    // de dos cifras tapa más que la esquina que no se compara y, sin este ícono en la biblioteca, salía carretilla.
    [Fact]
    public void La_captura_del_amigo_lee_el_aldeano_con_diez_en_cola()
    {
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "amigo-20261020-1080p-panel-negro.png"));

        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        var box = found.Layout.PixelsOf(RegionNames.ProductionQueue, screen.Width, screen.Height);
        Assert.Equal("villager", Assert.Single(Library.Read(screen.Crop(box).AtHudScale(found.Scale))).Key);
    }

    // La misma captura: el panel de puntajes (693/693 en rojo y 571/571 en azul, con el nick del amigo tapado) cae en su
    // caja escalada y se lee reescalado a 1080p, como en vivo.
    [Fact]
    public async Task La_captura_del_amigo_lee_el_panel_de_puntajes()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", "amigo-20261020-1080p-panel-negro.png"));
        var found = AutoCalibration.Detect(screen, RegionLayout.Default);

        Assert.NotNull(found);
        var box = found.Layout.PixelsOf(RegionNames.Scores, screen.Width, screen.Height);
        var rows = await new ScoresPanelReader(ocr).ReadAsync(screen.Crop(box).AtHudScale(found.Scale));

        Assert.Equal([(2, 693), (1, 571)], rows.Select(r => (r.Slot ?? 0, r.Score)));
    }
}
