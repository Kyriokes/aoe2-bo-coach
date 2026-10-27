using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>Una regla del generador: lo que agrega en un punto de decisión (y lo anota en el estado con su porqué).</summary>
internal interface IGoalRule
{
    IReadOnlyList<Step> Next(PlanView view, GoalState state);
}

/// <summary>
/// D12: lo que comparten las reglas del generador mientras se simula: los ids, el porqué de cada paso, lo agregado en
/// la consulta de ahora (que todavía no está en la foto) y lo que cada regla decidió.
/// </summary>
internal sealed class GoalState(GameData data, CivBonuses civ, AssistedPlan plan)
{
    private int _ids;

    public GameData Data { get; } = data;

    public CivBonuses Civ { get; } = civ;

    public AssistedPlan Plan { get; } = plan;

    /// <summary>El porqué de cada paso agregado, por id.</summary>
    public Dictionary<string, string> Reasons { get; } = [];

    /// <summary>Lo agregado en la consulta de ahora: la foto todavía no lo tiene.</summary>
    public List<Step> Added { get; } = [];

    public bool OpeningDone { get; set; }

    /// <summary>La fuente de comida a la que van los aldeanos nuevos (ovejas, jabalí, bayas, granjas).</summary>
    public GatherSource FoodSource { get; set; } = GatherSource.Sheep;

    /// <summary>Lo que otra regla avisa que hay que juntar pronto (el costo de la edad que se busca), para el reparto.</summary>
    public ResourceAmounts Planned { get; set; }

    /// <summary>Hasta cuántos aldeanos entran antes del próximo clic de edad (null = sin clic cerca).</summary>
    public int? VillagersBeforeClick { get; set; }

    public string NewId() => $"s{++_ids}";

    public Step Add(Step step, string reason)
    {
        Reasons[step.Id] = reason;
        Added.Add(step);
        return step;
    }

    /// <summary>Lo que tarda el centro urbano en sacar un aldeano, con la civ.</summary>
    public double VillagerTime(Age age) => Civ.Time(Data.Get("villager"), age) / Civ.WorkRate("town_center", age);

    /// <summary>Si la civ necesita casas (los hunos no).</summary>
    public bool MakesHouses => !Civ.Start.NoHouses && CivBuildings.For(Data, Civ, "house") is not null;

    /// <summary>Los pasos sin terminar: los de la foto y los agregados en esta consulta.</summary>
    public IEnumerable<Step> Pending(PlanView view) =>
        view.Steps.Where(s => s.End is null).Select(s => s.Step).Concat(Added);

    /// <summary>Si un paso pendiente (o agregado ahora) construye ese edificio o uno que cuenta como él.</summary>
    public bool Building(PlanView view, string key) =>
        Pending(view).Any(s => s.Do == StepAction.Build && s.What is { } what
            && (what == key || Data.TryGet(what, out var entity) && entity.CountsAs.Contains(key)));

    /// <summary>Si el edificio está hecho, en obra o pedido.</summary>
    public bool HasOrWillHave(PlanView view, string key) => view.Buildings.GetValueOrDefault(key) > 0 || Building(view, key);
}

/// <summary>Los edificios de cada civ: lo que construye para cumplir una clave (el asentamiento de los incas, el carro de mulas).</summary>
internal static class CivBuildings
{
    /// <summary>La clave misma si la civ la puede hacer; si no, un edificio de la civ que cuente como ella; null si nada.</summary>
    public static string? For(GameData data, CivBonuses civ, string key)
    {
        if (data.TryGet(key, out _) && data.CanMake(civ.Key, key))
            return key;
        return data.Entities.Values
            .FirstOrDefault(e => e.Kind == EntityKind.Building && e.CountsAs.Contains(key) && data.CanMake(civ.Key, e.Key))?.Key;
    }
}
