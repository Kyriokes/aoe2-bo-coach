using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Una tecnología que falta investigar y el edificio donde se investiga.</summary>
public sealed record TechReminder(string Tech, string Building);

/// <summary>
/// Las tecnologías que faltan con la BO terminada (pedido del usuario, 2026-10-26: "que muestre tecnologías sin
/// desarrollar relevantes"). Se pide una tecnología que importa (las económicas de siempre, las de un recurso con
/// aldeanos, las militares de las unidades de la postura), de la edad actual o de una anterior, con su edificio hecho,
/// que la civ tiene y no le sale gratis, que no se hizo y que alcanza para pagar. Una vez que aparece queda hasta que
/// se hace o se descarta ("Ya está"), así no parpadea si baja el stock.
/// <para>Hecha: la avisó el log, la hizo un paso de la BO, está en la cola de producción (dos lecturas seguidas) o el
/// stock bajó justo su costo. Las dos últimas solo la sacan por un rato (1,5 veces lo que tarda más 15 s): si el log no
/// la confirma, vuelve. Así un clic falso (dos aldeanos encolados son 100 de comida, lo mismo que la cota de escamas)
/// solo la demora. Si el stock vuelve justo su costo, se canceló.</para>
/// </summary>
public sealed class TechReminders(GameData data, string civ, IReadOnlyList<string> postureUnits)
{
    /// <summary>Cuántas se muestran a la vez.</summary>
    public const int MaxShown = 2;

    /// <summary>Aldeanos en madera, oro o piedra desde los que importan sus tecnologías.</summary>
    public const int GatherVillagers = 3;

    /// <summary>Aldeanos en comida desde los que importan las de las granjas (después de la BO, casi toda la comida es de granjas).</summary>
    public const int FarmVillagers = 6;

    private static readonly string[] Archers = ["archer", "skirmisher", "cavalry_archer"];
    private static readonly string[] Cavalry = ["scout_cavalry", "knight", "camel_rider", "battle_elephant", "steppe_lancer"];
    private static readonly string[] Infantry = ["militia", "spearman", "eagle_scout"];

    /// <summary>Por qué importa una tecnología: siempre, por los aldeanos en un recurso o por las unidades de la postura.</summary>
    private sealed record Candidate(string Tech, Resource? Gather = null, int Villagers = 0, string[]? Units = null);

    /// <summary>En orden: primero las económicas (las de siempre arriba), después las militares.</summary>
    private static readonly Candidate[] Candidates =
    [
        new("loom"),
        new("wheelbarrow"),
        new("double_bit_axe", Resource.Wood, GatherVillagers),
        new("horse_collar", Resource.Food, FarmVillagers),
        new("gold_mining", Resource.Gold, GatherVillagers),
        new("hand_cart"),
        new("bow_saw", Resource.Wood, GatherVillagers),
        new("heavy_plow", Resource.Food, FarmVillagers),
        new("gold_shaft_mining", Resource.Gold, GatherVillagers),
        new("stone_mining", Resource.Stone, GatherVillagers),
        new("stone_shaft_mining", Resource.Stone, GatherVillagers),
        new("fletching", Units: Archers),
        new("bloodlines", Units: [.. Cavalry, "cavalry_archer"]),
        new("forging", Units: [.. Infantry, .. Cavalry]),
        new("man_at_arms", Units: ["militia"]),
        new("crossbowman", Units: ["archer"]),
        new("light_cavalry", Units: ["scout_cavalry"]),
        new("padded_archer_armor", Units: Archers),
        new("scale_barding_armor", Units: Cavalry),
        new("scale_mail_armor", Units: Infantry),
        new("husbandry", Units: [.. Cavalry, "cavalry_archer"]),
    ];

    private readonly CivBonuses _civ = data.Civ(civ);
    private readonly List<string> _shown = [];
    private readonly HashSet<string> _dismissed = [];
    private readonly Dictionary<string, (double Until, bool Clicked)> _paused = [];
    private HashSet<string> _lastQueue = [];
    private ResourceAmounts? _lastStock;

    /// <summary>Las que se muestran ahora.</summary>
    public IReadOnlyList<TechReminder> Shown => _shown.Select(Reminder).ToList();

    /// <summary>
    /// Actualiza las que faltan con lo que se ve ahora. <paramref name="villagersOn"/> son los aldeanos en cada recurso
    /// (null si no se sabe: solo las de siempre); <paramref name="built"/> dice si hay un edificio hecho y
    /// <paramref name="byBuildOrder"/> si un paso de la BO ya la hizo (o la tiene en curso).
    /// </summary>
    public IReadOnlyList<TechReminder> Update(PlayerState state, IReadOnlyList<QueueItem>? queue, Age age, ResourceAmounts? villagersOn,
        Func<string, bool> built, Func<string, bool> byBuildOrder)
    {
        var now = state.Time;
        WatchStock(state.Stock, now, age);
        WatchQueue(queue, now, age);
        foreach (var (tech, pause) in _paused.ToList())
        {
            if (now >= pause.Until)
                _paused.Remove(tech);
        }
        bool Done(string tech) => _dismissed.Contains(tech) || _paused.ContainsKey(tech) || state.Count($"{tech}_researched") > 0 || byBuildOrder(tech);

        _shown.RemoveAll(Done);
        foreach (var candidate in Candidates)
        {
            if (_shown.Count >= MaxShown)
                break;
            if (!_shown.Contains(candidate.Tech) && !Done(candidate.Tech) && Wanted(candidate, state.Stock, age, villagersOn, built))
                _shown.Add(candidate.Tech);
        }
        return Shown;
    }

    /// <summary>"Ya está": descarta la primera que se muestra por el resto de la partida. False si no había ninguna.</summary>
    public bool Dismiss()
    {
        if (_shown.Count == 0)
            return false;
        _dismissed.Add(_shown[0]);
        _shown.RemoveAt(0);
        return true;
    }

    private bool Wanted(Candidate candidate, ResourceAmounts stock, Age age, ResourceAmounts? villagersOn, Func<string, bool> built)
    {
        if (!data.TryGet(candidate.Tech, out var entity) || entity.MadeIn is not { } building || entity.Age > age
            || !data.CanMake(civ, candidate.Tech))
            return false;
        var cost = _civ.Cost(entity, age);
        if (cost == ResourceAmounts.Zero || Enum.GetValues<Resource>().Any(r => stock[r] < cost[r]))
            return false;
        if (building != BuildOrderValidator.StartingBuilding && !built(building))
            return false;
        if (candidate.Units is { } units)
            return postureUnits.Any(units.Contains);
        return candidate.Gather is not { } resource || villagersOn is { } on && on[resource] >= candidate.Villagers;
    }

    /// <summary>
    /// El clic en una de las que se muestran: el stock bajó justo su costo (con el margen de los pasos de la BO, ver
    /// <see cref="Coach.Matches"/>). Si sube justo el costo de una pausada por clic, se canceló.
    /// </summary>
    private void WatchStock(ResourceAmounts stock, double now, Age age)
    {
        var previous = _lastStock;
        _lastStock = stock;
        if (previous is not { } before)
            return;
        var spent = before - stock;
        foreach (var (tech, pause) in _paused.Where(p => p.Value.Clicked).ToList())
        {
            if (data.TryGet(tech, out var entity) && Coach.Matches(spent * -1, _civ.Cost(entity, age)))
            {
                _paused.Remove(tech);
                return;
            }
        }
        foreach (var tech in _shown)
        {
            if (data.TryGet(tech, out var entity) && Coach.Matches(spent, _civ.Cost(entity, age)))
            {
                _paused[tech] = (now + Pause(entity, age), true);
                return;
            }
        }
    }

    /// <summary>Una tecnología en la cola dos lecturas seguidas se está investigando: queda pausada mientras siga ahí.</summary>
    private void WatchQueue(IReadOnlyList<QueueItem>? queue, double now, Age age)
    {
        var previous = _lastQueue;
        _lastQueue = queue?.Select(i => i.Key).ToHashSet() ?? [];
        foreach (var tech in _lastQueue.Where(previous.Contains))
        {
            if (data.TryGet(tech, out var entity) && entity.Kind == EntityKind.Tech)
                _paused[tech] = (now + Pause(entity, age), false);
        }
    }

    /// <summary>Cuánto se deja de pedir una tecnología cliqueada o en la cola sin que el log la confirme.</summary>
    private double Pause(Entity tech, Age age) => _civ.Time(tech, age) * 1.5 + 15;

    private TechReminder Reminder(string tech) => new(tech, data.Get(tech).MadeIn!);
}
