using System.IO;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;

namespace BoCoach.App;

/// <summary>Datos que comparten todas las ventanas: game_data, eventos, planner, validador y biblioteca.</summary>
public sealed class CoachData
{
    private CoachData(GameData gameData, EventCatalog events, BuildOrderLibrary library)
    {
        GameData = gameData;
        Events = events;
        Library = library;
        Planner = new Planner(gameData);
        Validator = new BuildOrderValidator(gameData, events);
    }

    public GameData GameData { get; }
    public EventCatalog Events { get; }
    public BuildOrderLibrary Library { get; }
    public Planner Planner { get; }
    public BuildOrderValidator Validator { get; }

    /// <summary>Letras del HUD (data/fonts); se cargan recién cuando se usa el coach.</summary>
    public HudFonts Fonts => _fonts ??= HudFonts.Load(Path.Combine(DataDir, "fonts"));

    /// <summary>Las aperturas típicas de cada civ (data/civ_styles.json), para sugerir la BO (D4).</summary>
    public CivStyles CivStyles => _civStyles ??= CivStyles.Load(Path.Combine(DataDir, "civ_styles.json"));

    private CivStyles? _civStyles;

    private HudFonts? _fonts;

    /// <summary>Carpeta "data" junto al ejecutable (game_data, events.json y BO de ejemplo).</summary>
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Carpeta del usuario: %APPDATA%\BoCoach.</summary>
    public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoCoach");

    /// <param name="libraryDir">Carpeta de la biblioteca; por defecto %APPDATA%\BoCoach\build_orders.</param>
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

    public static CoachData Load(string? libraryDir = null)
    {
        var gameData = GameDataLoader.LoadLatest(Path.Combine(DataDir, "game_data"));
        var events = EventCatalog.Load(Path.Combine(DataDir, "events.json")).PreferringEntitiesOf(gameData);
        var library = new BuildOrderLibrary(libraryDir ?? Path.Combine(UserDir, "build_orders"));
        library.SeedMissing(Directory.EnumerateFiles(Path.Combine(DataDir, "build_orders"), "*.json"));
        return new CoachData(gameData, events, library);
    }
}
