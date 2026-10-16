using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Wasm;

public static class Program
{
    /// <summary>No hace nada: el runtime queda cargado para las llamadas desde JavaScript.</summary>
    public static void Main()
    {
    }
}

/// <summary>Lo que la web puede llamar. Desde JavaScript: <c>exports.BoCoach.Wasm.Exports.Analyze(json)</c>.</summary>
[SupportedOSPlatform("browser")]
public static partial class Exports
{
    private static GameData? _data;
    private static EventCatalog? _events;

    /// <summary>Carga game_data y events.json (los mismos archivos que usa la app). Devuelve la versión del .dat.</summary>
    [JSExport]
    public static string Init(string gameDataJson, string eventsJson)
    {
        _data = GameDataLoader.Parse(gameDataJson);
        _events = EventCatalog.Parse(eventsJson);
        return _data.DatVersion;
    }

    /// <summary>Valida, describe y simula una BO (ver <see cref="BuildOrderAnalysis"/>).</summary>
    [JSExport]
    public static string Analyze(string buildOrderJson) =>
        BuildOrderAnalysis.Analyze(buildOrderJson, _data ?? throw new InvalidOperationException("Falta llamar a Init."), _events);
}
