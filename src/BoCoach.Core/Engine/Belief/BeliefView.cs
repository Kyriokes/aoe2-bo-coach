using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

public enum ResearchStatus
{
    /// <summary>Se está investigando (se la vio en la cola de producción).</summary>
    InProgress,

    Done,
}

/// <summary>
/// Lo que el coach cree de la partida en una captura (spec 2026-11-02): edificios por clave, investigaciones hechas y
/// en curso, unidades, lo último leído del HUD y la edad alcanzada. Es un registro inmutable: cada captura da uno nuevo.
/// </summary>
/// <param name="Buildings">Clave → cantidad creída. El centro urbano arranca en 1.</param>
/// <param name="Units">Clave → cantidad creída, sin aldeanos.</param>
/// <param name="Villagers">Total de aldeanos del HUD, o null si nunca se leyó.</param>
/// <param name="VillagersOn">Aldeanos por recurso del HUD, o null si nunca se leyeron.</param>
/// <param name="Population">"N" del HUD, o null si nunca se leyó.</param>
/// <param name="PopulationCap">"M" del HUD, o null si nunca se leyó.</param>
/// <param name="Age">La edad alcanzada según el log (una edad investigada, o algo avisado de una edad mayor).</param>
/// <param name="BuiltCap">Tope de población que dan los edificios creídos (centro urbano, casas, lo que suma población).</param>
public sealed record BeliefView(
    IReadOnlyDictionary<string, int> Buildings,
    IReadOnlyDictionary<string, ResearchStatus> Research,
    IReadOnlyDictionary<string, int> Units,
    int? Villagers,
    ResourceAmounts? VillagersOn,
    int? Population,
    int? PopulationCap,
    Age Age,
    int BuiltCap)
{
    public static readonly BeliefView Empty = new(
        new Dictionary<string, int> { [BuildOrders.BuildOrderValidator.StartingBuilding] = 1 },
        new Dictionary<string, ResearchStatus>(),
        new Dictionary<string, int>(),
        null, null, null, null, Age.Dark, PlanStart.Standard.PopulationCap);

    public int Count(string building) => Buildings.GetValueOrDefault(building);

    public bool InProgress(string tech) => Research.TryGetValue(tech, out var status) && status == ResearchStatus.InProgress;

    public bool Done(string tech) => Research.TryGetValue(tech, out var status) && status == ResearchStatus.Done;

    /// <summary>Unidades que no son aldeanos, todas juntas.</summary>
    public int Military => Units.Values.Sum();
}
