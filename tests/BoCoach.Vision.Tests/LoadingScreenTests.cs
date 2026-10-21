using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Pantalla de carga. carga-ia-en-1080p.png es la captura de diagnóstico del usuario (2026-10-20, partida rápida 4
/// contra 4 contra la IA, juego en inglés) con todo en negro menos el panel de jugadores: el arte del juego no va
/// al repo.
/// </summary>
public class LoadingScreenTests
{
    private static readonly GameData Data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data"));

    private static CapturedImage Screen(string name) =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);

    [Fact]
    public void El_panel_se_escala_por_el_alto_y_se_centra_a_lo_ancho()
    {
        Assert.Equal(new PixelRect(170, 190, 335, 740), LoadingScreen.Panel(1920, 1080));
        Assert.Equal(new PixelRect(227, 253, 446, 987), LoadingScreen.Panel(2560, 1440));
        // Más ancha que 16:9: 440 px de cada lado a 3440×1440.
        Assert.Equal(new PixelRect(440 + 227, 253, 446, 987), LoadingScreen.Panel(3440, 1440));
    }

    [Theory]
    [InlineData("TEAM 1", 1)]
    [InlineData("TEAM 2", 2)]
    [InlineData("TEAM l", 1)]
    [InlineData("Equipo 3", 3)]
    [InlineData("Team Fortress", null)]
    [InlineData("Kyriokes", null)]
    public void Reconoce_el_encabezado_de_cada_equipo(string text, int? team) => Assert.Equal(team, LoadingScreen.TeamOf(text));

    [Theory]
    [InlineData(0, 0, 204, 1)]
    [InlineData(183, 13, 13, 2)]
    [InlineData(10, 190, 10, 3)]
    [InlineData(196, 196, 26, 4)]
    [InlineData(4, 194, 184, 5)]
    [InlineData(198, 44, 198, 6)]
    [InlineData(108, 108, 108, 7)]
    [InlineData(182, 141, 32, 8)]
    [InlineData(20, 20, 20, null)] // el fondo oscuro no es ningún jugador
    public void El_numero_de_jugador_sale_del_color_de_su_cuadrado(byte r, byte g, byte b, int? slot) =>
        Assert.Equal(slot, LoadingScreen.SlotOf((r, g, b)));

    [Theory]
    [InlineData("JAPANESE", "en", "japanese")]
    [InlineData("Inca", "en", "incas")]
    [InlineData("Incas", "mx", "incas")]
    [InlineData("Borgoñones", "mx", "burgundians")]
    [InlineData("Borgofiones", "mx", "burgundians")]
    [InlineData("BÚLGAROS", "es", "bulgarians")]
    [InlineData("Bulgarlans", "en", "bulgarians")]
    [InlineData("Britanos", "en", "britons")] // con el idioma mal elegido, igual
    [InlineData("xyz", "en", null)]
    [InlineData("", "en", null)]
    public void Reconoce_la_civ_por_su_nombre(string text, string language, string? expected) =>
        Assert.Equal(expected, LoadingScreen.MatchCiv(text, Data, language));

    [Fact]
    public void Arma_los_jugadores_con_los_renglones_nombre_y_civ_bajo_cada_equipo()
    {
        (string, double)[] lines =
        [
            ("TEAM 1", 20), ("Kvriokes", 60), ("JAPANESE 1", 95), ("Kayqubad I", 135), ("TURKS 2", 170),
            ("TEAM 2", 350), ("Bulan Khagan", 395), ("CUMANS 53", 430), ("algo suelto", 470),
        ];
        var colors = new Dictionary<double, (byte, byte, byte)> { [95] = (0, 0, 204), [170] = (183, 13, 13), [430] = (4, 194, 184) };

        var players = LoadingScreen.Parse(lines, y => colors[y], Data, "en");

        Assert.Equal([(1, "Kvriokes", "japanese", 1), (2, "Kayqubad I", "turks", 1), (5, "Bulan Khagan", "cumans", 2)],
            players.Select(p => (p.Slot, p.Name, p.Civ, p.Team ?? 0)));
        Assert.Equal("CUMANS", players[2].CivText);
    }

    [Fact]
    public void Sin_encabezados_no_hay_equipos_y_un_color_raro_da_el_orden_de_la_lista()
    {
        (string, double)[] lines = [("Ana", 10), ("FRANKS", 40), ("Beto", 80), ("MAYANS", 110)];
        var players = LoadingScreen.Parse(lines, _ => Black, Data, "en");

        Assert.Equal([(1, (int?)null), (2, null)], players.Select(p => (p.Slot, p.Team)));
    }

    [Fact]
    public void Un_color_raro_toma_el_primer_numero_libre_sin_repetir_uno_ya_tomado()
    {
        // Antes tomaba "cuántos van + 1": con el verde (3) primero, el tercero de la lista volvía a ser el 3.
        (string, double)[] lines = [("Ana", 10), ("FRANKS", 40), ("Beto", 80), ("MAYANS", 110), ("Cata", 150), ("HUNS", 180)];
        var colors = new Dictionary<double, (byte, byte, byte)> { [40] = (0, 200, 0), [110] = Black, [180] = Black };

        var players = LoadingScreen.Parse(lines, y => colors[y], Data, "en");

        Assert.Equal([3, 1, 2], players.Select(p => p.Slot));
    }

    [Theory]
    [InlineData(new[] { "GAME MODE: RANDOM MAP", "ARABIA" }, "Arabia")]
    [InlineData(new[] { "MODO DE JUEGO: MAPA ALEATORIO", "ARENA" }, "Arena")]
    [InlineData(new[] { "GAME MODE: RANDOM MAP" }, null)]
    [InlineData(new string[0], null)]
    public void El_mapa_es_el_renglon_grande_debajo_del_modo_de_juego(string[] lines, string? map) =>
        Assert.Equal(map, LoadingScreen.MapFrom(lines));

    private static LoadingReading Reading(params (int Slot, string Name, int? Team)[] players) =>
        new(players.Select(p => new LoadingPlayer(p.Slot, p.Name, "", null, p.Team, Black)).ToList());

    [Fact]
    public void Encuentra_al_jugador_por_su_nombre_aunque_el_OCR_se_equivoque()
    {
        var reading = Reading((1, "Kvriokes", 1), (2, "Kayqubad I", 1), (3, "Bombarda88", 2));
        Assert.Equal(1, reading.Find("Kyriokes")?.Slot);
        Assert.Equal(3, reading.Find("bombarda 88")?.Slot);
        Assert.Equal(3, Reading((3, "B0mbarda8B", 1)).Find("Bombarda88")?.Slot);
        Assert.Null(reading.Find("Otro"));
        Assert.Null(reading.Find(""));
    }

    [Fact]
    public void Si_dos_nombres_se_parecen_igual_no_elige()
    {
        Assert.Null(Reading((1, "Halcon1", null), (2, "Halcon2", null)).Find("Halcon"));
    }

    [Fact]
    public void Rivales_por_equipo_y_sin_equipos_por_paridad()
    {
        var teams = Reading((1, "a", 1), (2, "b", 1), (5, "c", 2), (6, "d", 2));
        Assert.Equal([5, 6], teams.Opponents(teams.Players[0]).Select(p => p.Slot));
        Assert.Equal([2], teams.Allies(teams.Players[0]).Select(p => p.Slot));

        // Multijugador según el usuario: 1, 3, 5 y 7 contra 2, 4, 6 y 8.
        var none = Reading((1, "a", null), (2, "b", null), (3, "c", null), (4, "d", null));
        Assert.Equal([2, 4], none.Opponents(none.Players[0]).Select(p => p.Slot));
        Assert.Equal([3], none.Allies(none.Players[0]).Select(p => p.Slot));
    }

    // D4 (diseño del 2026-10-21): el rival de enfrente y la posición en el equipo.
    [Fact]
    public void Con_equipos_enfrente_es_el_del_mismo_lugar_en_el_otro_equipo()
    {
        var ai = Reading((1, "a", 1), (2, "b", 1), (3, "c", 1), (4, "d", 1), (5, "e", 2), (6, "f", 2), (7, "g", 2), (8, "h", 2));

        Assert.Equal(5, ai.Facing(ai.Players[0])?.Slot);
        Assert.Equal(7, ai.Facing(ai.Players[2])?.Slot);
        Assert.Equal(4, ai.Facing(ai.Players[7])?.Slot);
        Assert.Equal(TeamPosition.Flank, ai.PositionOf(ai.Players[0]));
        Assert.Equal(TeamPosition.Pocket, ai.PositionOf(ai.Players[1]));
        Assert.Equal(TeamPosition.Pocket, ai.PositionOf(ai.Players[2]));
        Assert.Equal(TeamPosition.Flank, ai.PositionOf(ai.Players[3]));
    }

    [Fact]
    public void Sin_equipos_enfrente_es_el_numero_de_al_lado_y_las_puntas_son_el_1_y_el_7()
    {
        // Multijugador según el usuario: 1, 3, 5 y 7 contra 2, 4, 6 y 8, el 1 enfrente del 2 y el 7 y el 8 en la otra punta.
        var none = Reading((1, "a", null), (2, "b", null), (3, "c", null), (4, "d", null), (5, "e", null), (6, "f", null), (7, "g", null), (8, "h", null));

        Assert.Equal(2, none.Facing(none.Players[0])?.Slot);
        Assert.Equal(3, none.Facing(none.Players[3])?.Slot);
        Assert.Equal(TeamPosition.Flank, none.PositionOf(none.Players[6]));
        Assert.Equal(TeamPosition.Pocket, none.PositionOf(none.Players[4]));

        // 1 contra 1 y 2 contra 2: sin posición.
        var duel = Reading((1, "a", null), (2, "b", null));
        Assert.Equal(2, duel.Facing(duel.Players[0])?.Slot);
        Assert.Null(duel.PositionOf(duel.Players[0]));
        Assert.Null(Reading((1, "a", null), (2, "b", null), (3, "c", null), (4, "d", null)).PositionOf(duel.Players[0]));
    }

    private static readonly string[] AiCivs = ["japanese", "turks", "britons", "khmer", "cumans", "romans", "tatars", "malians"];

    [Fact]
    public async Task Lee_los_8_jugadores_de_la_pantalla_real()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var reading = await new LoadingScreenReader(ocr, Data, "en").ReadAsync(Screen("carga-ia-en-1080p.png"));

        Assert.NotNull(reading);
        Assert.Equal(AiCivs, reading.Players.Select(p => p.Civ));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], reading.Players.Select(p => p.Slot));
        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 2], reading.Players.Select(p => p.Team));
        var me = reading.Find("Kyriokes");
        Assert.Equal(1, me?.Slot);
        Assert.Equal([5, 6, 7, 8], reading.Opponents(me!).Select(p => p.Slot));
    }

    [Fact]
    public async Task Lee_la_misma_pantalla_a_1440p()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return;
        var screen = Screen("carga-ia-en-1080p.png").Resample(1440 / 1080.0);
        var reading = await new LoadingScreenReader(ocr, Data, "en").ReadAsync(screen);

        Assert.NotNull(reading);
        Assert.Equal(AiCivs, reading.Players.Select(p => p.Civ));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], reading.Players.Select(p => p.Slot));
    }

    [Fact]
    public async Task Otra_pantalla_no_es_la_de_carga()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return;
        Assert.Null(await new LoadingScreenReader(ocr, Data).ReadAsync(Screen("amigo-20261020-1080p-panel-negro.png")));
    }
}
