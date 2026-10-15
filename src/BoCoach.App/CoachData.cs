using System.IO;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

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

    /// <summary>Carpeta "data" junto al ejecutable (game_data, events.json y BO de ejemplo).</summary>
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Carpeta del usuario: %APPDATA%\BoCoach.</summary>
    public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoCoach");

    /// <param name="libraryDir">Carpeta de la biblioteca; por defecto %APPDATA%\BoCoach\build_orders.</param>
    public static CoachData Load(string? libraryDir = null)
    {
        var gameData = GameDataLoader.LoadLatest(Path.Combine(DataDir, "game_data"));
        var events = EventCatalog.Load(Path.Combine(DataDir, "events.json"));
        var library = new BuildOrderLibrary(libraryDir ?? Path.Combine(UserDir, "build_orders"));
        library.SeedIfEmpty(Directory.EnumerateFiles(Path.Combine(DataDir, "build_orders"), "*.json"));
        return new CoachData(gameData, events, library);
    }
}
