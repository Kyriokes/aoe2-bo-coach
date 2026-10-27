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
    /// <summary>
    /// Lo que agrega en este punto de decisión, al final de la BO; vacío = nada por ahora. Los pasos de aldeanos van en
    /// unidades (cuántos aldeanos nuevos), como los ve en <see cref="PlanView.Steps"/>. El Planner guarda una copia: lo
    /// que la política cambie después en un paso que ya devolvió no cuenta.
    /// </summary>
    IReadOnlyList<Step> Next(PlanView view);

    /// <summary>
    /// La hora a la que la política pide que la vuelvan a consultar aunque no nazca nadie (null = no hace falta): por
    /// ejemplo, cuando se acaba una fuente de comida mientras el centro urbano investiga una edad. Lo que agregue
    /// entonces se fija con la hora.
    /// </summary>
    double? WakeAt => null;
}

/// <summary>
/// Un paso de la BO en la foto: lo que le falta encolar o empezar, lo que está en producción o en obra, si la BO ya
/// llegó a él, si arrancó, y cuándo empezó y terminó.
/// </summary>
public sealed record PlanViewStep(Step Step, int Remaining, bool Reached, bool Started, double? Start, double? End, int Unfinished = 0);

/// <summary>
/// Lo que ve la política en un punto de decisión: una foto de la simulación, de solo lectura. Las propiedades tienen
/// valores por defecto para poder armar una foto a mano en los tests de cada regla.
/// </summary>
public sealed record PlanView
{
    public double Time { get; init; }

    public ResourceAmounts Stock { get; init; }

    /// <summary>Población sin lo que está en producción (<see cref="InProduction"/>).</summary>
    public int Population { get; init; }

    public int InProduction { get; init; }

    public int PopulationCap { get; init; } = 5;

    public Age Age { get; init; } = Age.Dark;

    public IReadOnlyDictionary<GatherSource, int> Villagers { get; init; } = new Dictionary<GatherSource, int>();

    public int IdleVillagers { get; init; }

    public int Builders { get; init; }

    /// <summary>Edificios hechos por clave, contando lo que vale por varios (el Settlement).</summary>
    public IReadOnlyDictionary<string, int> Buildings { get; init; } = new Dictionary<string, int>();

    /// <summary>Edificios en obra por clave.</summary>
    public IReadOnlyDictionary<string, int> UnderConstruction { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<string> Researched { get; init; } = [];

    /// <summary>Lo que junta un aldeano por segundo en cada fuente, con la civ, la edad y las tecnologías.</summary>
    public IReadOnlyDictionary<GatherSource, double> Rates { get; init; } = new Dictionary<GatherSource, double>();

    /// <summary>Unidades y tecnologías esperando en la cola de cada edificio.</summary>
    public IReadOnlyDictionary<string, int> Queued { get; init; } = new Dictionary<string, int>();

    /// <summary>Edificios de cada clave trabajando ahora.</summary>
    public IReadOnlyDictionary<string, int> Busy { get; init; } = new Dictionary<string, int>();

    /// <summary>Todos los pasos hasta ahora, en orden, con su estado.</summary>
    public IReadOnlyList<PlanViewStep> Steps { get; init; } = [];

    /// <summary>En este segundo nació alguien (sigue en verdadero en las consultas encadenadas del mismo segundo).</summary>
    public bool Birth { get; init; }

    /// <summary>La BO no tiene pasos por arrancar.</summary>
    public bool RanOut { get; init; }

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
