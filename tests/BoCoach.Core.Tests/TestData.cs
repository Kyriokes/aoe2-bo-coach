using BoCoach.Core.Data;

namespace BoCoach.Core.Tests;

/// <summary>Acceso a los archivos reales del repo copiados a la salida de los tests.</summary>
internal static class TestData
{
    public static string GameDataDir => Path.Combine(AppContext.BaseDirectory, "game_data");

    private static readonly Lazy<GameData> Latest = new(() => GameDataLoader.LoadLatest(GameDataDir));

    /// <summary>game_data del último parche exportado.</summary>
    public static GameData GameData => Latest.Value;

    private static readonly Lazy<EventCatalog> LazyEvents = new(() => EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, "events.json")));

    public static EventCatalog Events => LazyEvents.Value;

    public static string BuildOrdersDir => Path.Combine(AppContext.BaseDirectory, "build_orders");

    public static IEnumerable<string> ExampleBuildOrders => Directory.EnumerateFiles(BuildOrdersDir, "*.json");
}
