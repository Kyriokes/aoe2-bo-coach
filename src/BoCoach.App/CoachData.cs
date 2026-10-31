using System.IO;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;

namespace BoCoach.App;

/// <summary>Datos que comparten todas las ventanas: game_data, eventos, planner y validador.</summary>
public sealed class CoachData
{
    private CoachData(GameData gameData, EventCatalog events, Maps maps)
    {
        GameData = gameData;
        Events = events;
        Maps = maps;
        Planner = new Planner(gameData);
        Validator = new BuildOrderValidator(gameData, events, maps);
    }

    public GameData GameData { get; }
    public EventCatalog Events { get; }

    /// <summary>Los mapas (data/maps.json): qué fuentes tiene cada uno y si hay agua (D8).</summary>
    public Maps Maps { get; }

    public Planner Planner { get; }
    public BuildOrderValidator Validator { get; }

    /// <summary>Letras del HUD (data/fonts); se cargan recién cuando se usa el coach.</summary>
    public HudFonts Fonts => _fonts ??= HudFonts.Load(Path.Combine(DataDir, "fonts"));

    /// <summary>El diseño del panel del mod (data/hud_layout.json): de él salen el mod, la calibración sola y el lector.</summary>
    public HudLayout Layout => _layout ??= HudLayout.Load(Path.Combine(DataDir, HudLayout.FileName));

    private HudLayout? _layout;

    /// <summary>Las aperturas típicas de cada civ (data/civ_styles.json), para sugerir la BO (D4).</summary>
    public CivStyles CivStyles => _civStyles ??= CivStyles.Load(Path.Combine(DataDir, "civ_styles.json"));

    private CivStyles? _civStyles;

    /// <summary>
    /// D10: las estadísticas reales de cruces (data/matchup_stats.json) de esa civ, cargadas una vez por civ y solo
    /// con lo suyo (leer el archivo tarda unos 170 ms; se hace en la pantalla de carga).
    /// </summary>
    public MatchupStats Matchups(string civ)
    {
        var key = civ.Trim().ToLowerInvariant();
        if (!_matchups.TryGetValue(key, out var stats))
            _matchups[key] = stats = MatchupStats.Load(Path.Combine(DataDir, "matchup_stats.json"), onlyCiv: key);
        return stats;
    }

    private readonly Dictionary<string, MatchupStats> _matchups = new();

    private HudFonts? _fonts;

    /// <summary>Carpeta "data" junto al ejecutable (game_data, events.json, letras, íconos).</summary>
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Carpeta del usuario: %APPDATA%\BoCoach.</summary>
    public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoCoach");

    private readonly Dictionary<string, EventCatalog> _eventsByLanguage = [];

    /// <summary>
    /// Las frases del log para el juego en ese idioma (las inglesas más las del idioma). <see cref="Events"/> es el
    /// inglés: los ids son los mismos, así que el validador y el editor no dependen del idioma.
    /// </summary>
    public EventCatalog EventsFor(GameLanguage language)
    {
        if (language.EventsFile is null)
            return Events;
        lock (_eventsByLanguage)
        {
            if (!_eventsByLanguage.TryGetValue(language.Code, out var events))
                _eventsByLanguage[language.Code] = events = language.LoadEvents(DataDir, GameData);
            return events;
        }
    }

    public static CoachData Load()
    {
        var gameData = GameDataLoader.LoadLatest(Path.Combine(DataDir, "game_data"));
        var events = EventCatalog.Load(Path.Combine(DataDir, "events.json")).PreferringEntitiesOf(gameData);
        var maps = Maps.Load(Path.Combine(DataDir, "maps.json"));
        return new CoachData(gameData, events, maps);
    }
}
