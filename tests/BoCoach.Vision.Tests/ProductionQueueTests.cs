using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.Vision.Tests;

public class ProductionQueueTests
{
    private static readonly IconLibrary Library = IconLibrary.Load(Path.Combine(AppContext.BaseDirectory, "icons", "production_queue.json"));

    private static CapturedImage Frame(int frame, string recording = "20261018-021924") =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", $"queue-{recording}", $"{frame:D6}_production_queue.png"));

    // Partida 20261018-132830: lo que espera ("…", teñido de amarillo o naranja) y dos galerías sacando arqueros.
    [Theory]
    [InlineData(32, "")]                               // 0:00, el adorno del panel vacío no es un ícono
    [InlineData(280, "villager loom…")]                // el telar espera detrás del aldeano
    [InlineData(690, "villager feudal_age…")]          // la Feudal espera (naranja): antes se leía como aldeano
    [InlineData(946, "villager archer archer")]        // 12:14, un arquero en cada galería
    public void Distingue_lo_que_espera_de_lo_que_avanza(int frame, string expected)
    {
        Assert.Equal(expected, string.Join(" ", Library.Read(Frame(frame, "20261018-132830"))));
    }

    // Partida 20261018-021924, mirando las capturas (y el log para saber qué se investigaba).
    [Theory]
    [InlineData(330, "loom villager…")]                        // 4:00, el aldeano espera detrás del telar (amarillo)
    [InlineData(700, "feudal_age")]                            // subiendo a Feudal
    [InlineData(1060, "villager double_bit_axe horse_collar archer archer")]
    [InlineData(1440, "villager archer archer")]               // con el mapa detrás
    [InlineData(1510, "wheelbarrow")]                          // la carretilla, que la BO no tenía en curso
    [InlineData(1578, "")]                                     // nada en producción
    public void Lee_la_cola_de_produccion(int frame, string expected)
    {
        Assert.Equal(expected, string.Join(" ", Library.Read(Frame(frame))));
    }

    [Fact]
    public void Los_iconos_vienen_con_los_datos()
    {
        var fonts = HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts"));

        Assert.NotNull(fonts.QueueIcons);
    }
}
