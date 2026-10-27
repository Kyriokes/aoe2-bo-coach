using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// D12: una política que arma la BO mientras el Planner simula (el generador del modo asistido). En cada punto de
/// decisión (un nacimiento, la BO sin pasos por arrancar, todo terminado) recibe una foto y devuelve los pasos que se
/// agregan al final. El Planner fija los que lo necesitan (ver <see cref="Planner"/>) para que la BO que sale, simulada
/// de nuevo sin la política, dé los mismos tiempos.
/// </summary>
public interface IStepPolicy
{
    /// <summary>Lo que agrega en este punto de decisión, al final de la BO; vacío = nada por ahora.</summary>
    IReadOnlyList<Step> Next(PlanView view);
}

/// <summary>Un paso de la BO en la foto: lo que le falta, si la BO ya llegó a él, si arrancó, y cuándo empezó y terminó.</summary>
public sealed record PlanViewStep(Step Step, int Remaining, bool Reached, bool Started, double? Start, double? End);

/// <summary>Lo que ve la política en un punto de decisión: una foto de la simulación, de solo lectura.</summary>
/// <param name="Population">Población sin lo que está en producción (<paramref name="InProduction"/>).</param>
/// <param name="Buildings">Edificios hechos por clave, contando lo que vale por varios (el Settlement).</param>
/// <param name="UnderConstruction">Edificios en obra por clave.</param>
/// <param name="Rates">Lo que junta un aldeano por segundo en cada fuente, con la civ, la edad y las tecnologías.</param>
/// <param name="Queued">Unidades y tecnologías esperando en la cola de cada edificio.</param>
/// <param name="Busy">Edificios de cada clave trabajando ahora.</param>
/// <param name="Steps">Todos los pasos hasta ahora, en orden, con su estado.</param>
/// <param name="Birth">En este segundo nació alguien.</param>
/// <param name="RanOut">La BO no tiene pasos por arrancar.</param>
public sealed record PlanView(
    double Time,
    ResourceAmounts Stock,
    int Population,
    int InProduction,
    int PopulationCap,
    Age Age,
    IReadOnlyDictionary<GatherSource, int> Villagers,
    int IdleVillagers,
    int Builders,
    IReadOnlyDictionary<string, int> Buildings,
    IReadOnlyDictionary<string, int> UnderConstruction,
    IReadOnlyList<string> Researched,
    IReadOnlyDictionary<GatherSource, double> Rates,
    IReadOnlyDictionary<string, int> Queued,
    IReadOnlyDictionary<string, int> Busy,
    IReadOnlyList<PlanViewStep> Steps,
    bool Birth,
    bool RanOut)
{
    /// <summary>La comida que queda, con la comida finita; null con la infinita.</summary>
    public PlanFoodView? Food { get; init; }
}

/// <summary>La comida que queda en la foto de la política (solo con la comida finita, D12).</summary>
/// <param name="Remaining">
/// Ovejas, caza y bayas: la comida que todavía rinden para la civ en esta edad. Lo que no está no se acaba (madera,
/// oro, piedra, pesca).
/// </param>
/// <param name="Farms">Lo que le queda a la granja de cada granjero, en orden.</param>
/// <param name="FarmersWaitingReseed">Granjeros con la granja vacía, esperando madera para resembrarla.</param>
/// <param name="FarmersWaitingFarm">Granjeros sin granja que esperan la de su paso de granjas.</param>
public sealed record PlanFoodView(
    IReadOnlyDictionary<GatherSource, double> Remaining,
    IReadOnlyList<double> Farms,
    int FarmersWaitingReseed,
    int FarmersWaitingFarm);
