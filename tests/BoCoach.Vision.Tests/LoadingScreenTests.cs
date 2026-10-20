using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;

namespace BoCoach.Vision.Tests;

/// <summary>
/// Pantalla de carga. Las capturas de prueba son sintéticas (dibujadas con las posiciones de
/// widgetui/screenloadgametransition.json, sin nada del juego): falta una captura real para verificar colores,
/// letra y escala.
/// </summary>
public class LoadingScreenTests
{
    private static readonly GameData Data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data"));

    private static CapturedImage Screen(string name) =>
        Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));

    private static readonly (byte R, byte G, byte B) Black = (0, 0, 0);

    [Fact]
    public void Las_posiciones_salen_del_diseno_a_3840x2160_escalado_por_el_alto()
    {
        // Renglón 1: ancla del panel (700, 765) + ancla del renglón (10, 100); el nombre arranca en 210 (con margen, 205).
        Assert.Equal(new PixelRect(458, 433, 325, 41), LoadingScreen.Name(1, 1920, 1080));
        Assert.Equal(new PixelRect(610, 997, 433, 54), LoadingScreen.Name(8, 2560, 1440));
        // Una pantalla más ancha que 16:9 se supone centrada a lo ancho: 440 px de cada lado a 3440×1440.
        Assert.Equal(new PixelRect(440 + 610, 577, 433, 54), LoadingScreen.Name(1, 3440, 1440));
    }

    [Theory]
    [InlineData("Starting Game", true)]
    [InlineData("Startinq Gane", true)]
    [InlineData("Iniciando partida", true)]
    [InlineData("INICIANDO PARTIDA...", true)]
    [InlineData("Game Settings", false)]
    [InlineData("", false)]
    public void Reconoce_el_titulo(string text, bool expected) => Assert.Equal(expected, LoadingScreen.IsTitle(text));

    [Theory]
    [InlineData("Inca", "en", "incas")]
    [InlineData("Incas", "mx", "incas")]
    [InlineData("Borgoñones", "mx", "burgundians")]
    [InlineData("Borgofiones", "mx", "burgundians")]
    [InlineData("Búlgaros", "es", "bulgarians")]
    [InlineData("Bulgarlans", "en", "bulgarians")]
    [InlineData("Burmese", "en", "burmese")]
    [InlineData("Malians", "en", "malians")]
    [InlineData("Britanos", "en", "britons")] // con el idioma mal elegido, igual
    [InlineData("xyz", "en", null)]
    [InlineData("", "en", null)]
    public void Reconoce_la_civ_por_su_nombre(string text, string language, string? expected) =>
        Assert.Equal(expected, LoadingScreen.MatchCiv(text, Data, language));

    [Theory]
    [InlineData(new[] { "Game Settings", "Arabia" }, "Arabia")]
    [InlineData(new[] { "Ajustes del juego", "Arena" }, "Arena")]
    [InlineData(new[] { "Arabia" }, "Arabia")]
    [InlineData(new[] { "Game Settinqs" }, null)]
    public void El_mapa_es_el_renglon_que_no_es_el_titulo(string[] lines, string? expected) =>
        Assert.Equal(expected, LoadingScreen.MapFrom(lines));

    [Theory]
    [InlineData("1 Ferrus", 1, "Ferrus")]
    [InlineData("2 Lady Halcon", 2, "Lady Halcon")]
    [InlineData("l Ferrus", 1, "Ferrus")] // el OCR confunde el 1 con la l
    [InlineData("- Ferrus", null, "Ferrus")] // sin equipo
    [InlineData("Ferrus", null, "Ferrus")]
    [InlineData("88Bombarda", null, "88Bombarda")]
    [InlineData("", null, "")]
    public void Separa_el_equipo_del_nombre(string text, int? team, string name) =>
        Assert.Equal((team, name), LoadingScreenReader.SplitTeam(text));

    private static LoadingReading Reading(params (int Slot, string Name, int? Team)[] players) =>
        new(players.Select(p => new LoadingPlayer(p.Slot, p.Name, "", null, p.Team, Black)).ToList(), null);

    [Fact]
    public void Encuentra_al_jugador_por_su_nombre_aunque_el_OCR_se_equivoque()
    {
        var reading = Reading((1, "Ferrus", 1), (2, "AI Arturo", 2), (3, "Bombarda88", 1));
        Assert.Equal(3, reading.Find("Bombarda88")?.Slot);
        Assert.Equal(3, reading.Find("bombarda 88")?.Slot);
        Assert.Equal(1, reading.Find("Ferrus")?.Slot);
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
    public void Rivales_por_equipo_y_si_no_se_leyo_ninguno_por_paridad()
    {
        var teams = Reading((1, "a", 1), (2, "b", 2), (3, "c", 2), (4, "d", 1));
        Assert.Equal([2, 3], teams.Opponents(teams.Players[0]).Select(p => p.Slot));
        Assert.Equal([4], teams.Allies(teams.Players[0]).Select(p => p.Slot));

        var unread = Reading((1, "a", null), (2, "b", null), (3, "c", null), (4, "d", null));
        Assert.Equal([2, 4], unread.Opponents(unread.Players[0]).Select(p => p.Slot));
        Assert.Equal([3], unread.Allies(unread.Players[0]).Select(p => p.Slot));
    }

    [Fact]
    public void Una_celda_vacia_no_tiene_texto()
    {
        var screen = Screen("carga-sintetica-en-1080p.png");
        Assert.True(LoadingScreenReader.HasText(screen.Crop(LoadingScreen.Name(1, 1920, 1080))));
        Assert.False(LoadingScreenReader.HasText(screen.Crop(new PixelRect(400, 850, 300, 41))));
    }

    [Fact]
    public async Task Lee_los_8_jugadores_y_el_mapa_en_ingles()
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var reading = await new LoadingScreenReader(ocr, Data, "en").ReadAsync(Screen("carga-sintetica-en-1080p.png"));

        Assert.NotNull(reading);
        Assert.Equal(["incas", "britons", "franks", "burgundians", "bulgarians", "burmese", "magyars", "malians"],
            reading.Players.Select(p => p.Civ));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], reading.Players.Select(p => p.Slot));
        Assert.Equal([1, 2, 1, 2, 1, 2, 1, 2], reading.Players.Select(p => p.Team));
        Assert.Equal(3, reading.Find("Bombarda88")?.Slot);
        Assert.Equal("Arabia", reading.Map);
        var red = reading.Players[1].Color;
        Assert.True(red.R > 200 && red.G < 60 && red.B < 60, $"color del jugador 2: {red}");
    }

    [Fact]
    public async Task Lee_la_pantalla_en_castellano_a_1440p()
    {
        var ocr = OcrReader.TryCreate("es-MX") ?? OcrReader.TryCreate("es-ES") ?? OcrReader.TryCreate();
        if (ocr is null)
            return;
        var reading = await new LoadingScreenReader(ocr, Data, "mx").ReadAsync(Screen("carga-sintetica-mx-1440p.png"));

        Assert.NotNull(reading);
        Assert.Equal(["burgundians", "bulgarians", "incas", "hindustanis"], reading.Players.Select(p => p.Civ));
        Assert.Equal(2, reading.Find("Ferrus")?.Slot);
        Assert.Equal([1, 3], reading.Opponents(reading.Find("Ferrus")!).Select(p => p.Slot));
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
