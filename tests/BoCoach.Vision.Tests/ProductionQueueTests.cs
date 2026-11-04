using BoCoach.Core.Data;
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

    // Partida 20261101-230201 (aztecas, drush): la milicia no estaba en la biblioteca y, según cuánto verde tenía, se leía
    // arquero, ballestero, arquero a caballo o carretilla: el coach daba por hecha la galería a las 9:37 (antes de Feudal)
    // y la carretilla en curso. El arquero recién encolado (borde blanco) salía "?".
    [Theory]
    [InlineData(718, "villager militia")]                      // 9:38, recién encolada, con el borde blanco
    [InlineData(734, "villager militia")]                      // a medio llenar de verde (se leía ballestero)
    [InlineData(758, "villager militia")]                      // casi lista (se leía arquero a caballo)
    [InlineData(762, "villager housed")]                       // la siguiente en rojo: sin población
    [InlineData(1172, "archer")]                               // 15:46, el arquero recién encolado
    public void Lee_la_milicia_en_todos_sus_tonos(int frame, string expected)
    {
        Assert.Equal(expected, string.Join(" ", Library.Read(Frame(frame, "20261101-230201"))));
    }

    // Partida 20261104-142003 (jugador naranja): el aldeano recién empezado con la cantidad en la esquina salía "?" unos
    // 12 s cada 50 s, a 0,10 o 0,13 de lo que conocía la biblioteca (el umbral es 0,09).
    [Theory]
    [InlineData(5)]
    [InlineData(64)]
    [InlineData(130)]
    public void Lee_al_aldeano_naranja_recien_empezado(int frame)
    {
        Assert.Equal("villager", string.Join(" ", Library.Read(Frame(frame, "20261104-142003"))));
    }

    // Partida 20261029-211516 (coreanos, v0.9.44 con la caja de la cola en x 0): la carretilla y Castillos esperando,
    // amarillos, se leían como la Feudal en espera.
    [Theory]
    [InlineData(1064, "villager archer archer")]
    [InlineData(1558, "villager archer archer wheelbarrow…")]
    [InlineData(1812, "villager archer archer castle_age…")]
    public void Distingue_las_investigaciones_que_esperan(int frame, string expected)
    {
        Assert.Equal(expected, string.Join(" ", Library.Read(Frame(frame, "20261029-211516"))));
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

/// <summary>
/// Los íconos de los archivos del juego (data/icons/queue_game.json): lo que la cola reconoce sin haberlo visto nunca en
/// una partida. En las capturas de estos tests la biblioteca de muestras reales cortaba la lectura en el primer ícono
/// que no conocía, y el log confirma cada tecnología.
/// </summary>
public class QueueIconCatalogTests
{
    private static string Icons(string file) => Path.Combine(AppContext.BaseDirectory, "icons", file);

    private static readonly IconLibrary Full = IconLibrary.Load(Icons("production_queue.json"));
    private static readonly IconLibrary SamplesOnly = IconLibrary.Load(Icons("production_queue.json"), withGame: false);

    private static CapturedImage Frame(string recording, int frame) =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", $"queue-{recording}", $"{frame:D6}_production_queue.png"));

    [Theory]
    [InlineData("20261102-212823", 1016, "villager double_bit_axe horse_collar gold_mining", "villager double_bit_axe horse_collar")]
    [InlineData("20261101-165020", 1498, "villager padded_archer_armor thumb_ring", "villager")]
    [InlineData("20261101-165020", 1528, "villager padded_archer_armor thumb_ring bow_saw heavy_plow ? cavalry_archer ? bodkin_arrow", "villager")]
    public void Lee_lo_que_nunca_se_vio_en_una_partida(string recording, int frame, string expected, string before)
    {
        Assert.Equal(expected, string.Join(" ", Full.Read(Frame(recording, frame)).Select(i => i.Key)));
        Assert.Equal(before, string.Join(" ", SamplesOnly.Read(Frame(recording, frame)).Select(i => i.Key)));
    }

    // Todas las muestras son de partidas en azul. La hoja las trae en los 8 colores, una por celda de 33 px: a cada muestra
    // se le suma lo que cambia entre el azul y ese color en el ícono del juego (el canal alfa dice cuánto de cada celda es
    // color del jugador, y el color es el primero de resources/_common/palettes/playercolor_*.pal), así que conserva el
    // ruido de una captura real. Sin comparar aparte las celdas del color del jugador salían 17 "?", casi todos en
    // amarillo y en naranja.
    [Fact]
    public void Una_muestra_en_azul_sirve_para_cualquier_color_de_jugador()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "queue-colores");
        var sheet = Png.Read(Path.Combine(folder, "muestras-en-8-colores.png"));
        var size = ProductionQueue.IconSize;
        var columns = sheet.Width / size;
        var wrong = File.ReadAllLines(Path.Combine(folder, "muestras-en-8-colores.txt")).Select((line, cell) =>
        {
            var (x, y) = (cell % columns * size, cell / columns * size);
            var match = Full.Match(ProductionQueue.Features(sheet, x, y), framed: ProductionQueue.HasIconFrame(sheet, x, y));
            return match is { Distance: <= IconLibrary.MatchThreshold } m && m.Key == line.Split(' ')[0] ? null : $"{cell} {line}: {match}";
        }).OfType<string>().ToList();

        Assert.Empty(wrong);
    }

    // La partida de catálogo: persas en violeta (jugador 6), con todo el árbol encolado.
    [Theory]
    [InlineData(4074, 8)] // el primero a medio teñir no se reconoce: la cola entera salía vacía
    [InlineData(3423, 8)] // los dos íconos parejos en espera se parecían a la arena y cortaban la lectura
    [InlineData(1908, 11)]
    [InlineData(2014, 5)]
    public void Lee_toda_la_cola_aunque_no_reconozca_algun_icono(int frame, int expected) =>
        Assert.Equal(expected, Full.Read(Frame("20261103-193834", frame)).Count);

    [Fact]
    public void Un_icono_oscuro_en_espera_se_ve_en_espera()
    {
        var read = Full.Read(Frame("20261103-193834", 1908));

        Assert.All(new[] { read[6], read[7], read[10] }, item => Assert.True(item.Waiting, item.Key));
        Assert.DoesNotContain(read.Skip(6), item => item.Key == "loom"); // el telar ya estaba hecho desde las 3:20
    }

    [Fact]
    public void Los_iconos_en_espera_no_se_toman_por_lo_que_mas_se_les_parece_en_color()
    {
        // Eran "feudal_age…" y "crossbowman…": la Feudal se había terminado hacía 24 minutos.
        var read = Full.Read(Frame("20261103-193834", 2490));

        Assert.DoesNotContain(read.Where(item => item.Waiting), item => item.Key is "feudal_age" or "crossbowman");
    }

    [Fact]
    public void Lee_el_aldeano_en_violeta()
    {
        var read = Full.Read(Frame("20261103-193834", 2014));

        Assert.Equal(["villager", "villager", "villager"], read.Take(3).Select(i => i.Key));
    }

    [Fact]
    public void Un_icono_con_marco_que_no_se_reconoce_no_corta_lo_que_viene_despues()
    {
        var empty = new IconLibrary([], new QueueIconCatalog([]));
        var read = new IconLibrary(SamplesOnly.Icons, new QueueIconCatalog([])).Read(Frame("20261101-165020", 1528));

        Assert.Empty(empty.Read(Frame("20261101-165020", 1528)));
        Assert.Equal("villager", read[0].Key);
        Assert.Contains(IconLibrary.Unknown, read.Select(i => i.Key));
        Assert.Contains("cavalry_archer", read.Select(i => i.Key));
    }

    [Fact]
    public void El_pasto_oscuro_no_tiene_marco_y_un_icono_si()
    {
        var image = Frame("20261018-132830", 690);

        Assert.True(ProductionQueue.HasIconFrame(image, 0, 0));
        Assert.True(ProductionQueue.HasIconFrame(image, ProductionQueue.IconSize, 0));
        Assert.False(ProductionQueue.HasIconFrame(image, 2 * ProductionQueue.IconSize, 0));
    }

    [Fact]
    public void El_color_del_jugador_no_cambia_de_que_unidad_se_trata()
    {
        var icon = Plain("unidad", 0.5f, skip: [60]);
        var catalog = new QueueIconCatalog([icon, Plain("otra", 0.1f, skip: [])]);
        var red = (float[])icon.Features.Clone();
        (red[180], red[181], red[182]) = (1, 0, 0);

        Assert.Equal("unidad", catalog.Best(red, 0.09f)?.Key);
        Assert.Equal(0, catalog.Best(red, 0.09f)!.Value.Distance, 3);
    }

    [Fact]
    public void Reconoce_el_ícono_en_cada_estado_y_prefiere_lo_que_una_BO_puede_pedir()
    {
        var common = Plain("arquero", 0.5f, skip: []);
        var catalog = new QueueIconCatalog([common, Plain("mangudai", 0.5f, skip: [], isCommon: false)]);

        Assert.Equal("arquero", catalog.Best(common.Features, 0.09f)?.Key);
        // Amarillo: el tinte del ícono entero, con el mismo cálculo que hace el catálogo.
        var yellow = (float[])common.Features.Clone();
        for (var i = 0; i < yellow.Length; i += 3)
            (yellow[i], yellow[i + 1], yellow[i + 2]) = (0.5f + 0.529f * 0.5f, 0.324f + 0.432f * 0.5f, 0.258f * 0.5f);
        Assert.Equal("arquero", catalog.Best(yellow, 0.09f)?.Key);
    }

    [Fact]
    public void En_rojo_es_sin_poblacion_sea_cual_sea_la_unidad()
    {
        var samples = SamplesOnly.Icons;

        Assert.All(samples.Where(i => i.Key == IconLibrary.Housed), i => Assert.True(ProductionQueue.LooksHoused(i.Features)));
        Assert.All(samples.Where(i => i.Key != IconLibrary.Housed), i => Assert.False(ProductionQueue.LooksHoused(i.Features)));
    }

    [Fact]
    public void Los_archivos_del_juego_traen_las_unidades_y_tecnologias_de_siempre()
    {
        var catalog = QueueIconCatalog.TryLoad(Icons("queue_game.json"));
        var data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data"));

        Assert.NotNull(catalog);
        Assert.Empty(data.Entities.Values.Where(e => e.Kind != EntityKind.Building).Select(e => e.Key).Except(catalog.Keys));
    }

    private static GameQueueIcon Plain(string key, float value, int[] skip, bool isCommon = true) =>
        new(key, true, isCommon, skip, [.. Enumerable.Repeat(value, ProductionQueue.Grid * ProductionQueue.Grid * 3)]);
}
