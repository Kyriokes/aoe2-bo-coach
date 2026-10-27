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

    private static readonly string[] AgeTechs = ["feudal_age", "castle_age", "imperial_age"];

    /// <summary>
    /// Los tipos de objetivo que el generador ya arma (los demás se avisan y no frenan a los de abajo). Cada sub-tarea
    /// del (d) suma el suyo.
    /// </summary>
    public static readonly IReadOnlySet<string> Generated = new HashSet<string> { "age" };

    /// <summary>El tipo de un objetivo: "age", "unit", "town_centers" o "tech".</summary>
    public static string Kind(AssistedGoal goal) =>
        goal.Age is not null ? "age" : goal.Unit is not null ? "unit" : goal.TownCenters is not null ? "town_centers" : "tech";

    /// <summary>Los objetivos que no se pueden, con el motivo (por índice): la civ no los tiene, o piden una edad que ningún objetivo de antes pide.</summary>
    public IReadOnlyDictionary<int, string> Skipped => _skipped ??= FindSkipped();

    private Dictionary<int, string>? _skipped;

    /// <summary>Los edificios que piden los objetivos (el de cada unidad y tecnología): para subir de edad se prefieren.</summary>
    public IReadOnlySet<string> WantedBuildings => _wanted ??= Plan.Goals
        .Select((g, i) => (g, i))
        .Where(x => !Skipped.ContainsKey(x.i) && (x.g.Unit ?? x.g.Tech) is { } key && Data.TryGet(key, out _))
        .Select(x => Data.Get((x.g.Unit ?? x.g.Tech)!).MadeIn)
        .OfType<string>()
        .ToHashSet();

    private HashSet<string>? _wanted;

    /// <summary>Los objetivos que ya agregaron todos sus pasos (los de edad se miran en los pasos: ver <see cref="Launched"/>).</summary>
    public HashSet<int> LaunchedGoals { get; } = [];

    /// <summary>
    /// El objetivo que se trabaja ahora: el primero que no arrancó todos sus pasos (spec, sección 3: "un objetivo empieza
    /// a trabajarse cuando el anterior ya arrancó todos sus pasos"). Null si arrancaron todos.
    /// </summary>
    public int? ActiveGoal(PlanView view)
    {
        for (var i = 0; i < Plan.Goals.Count; i++)
        {
            if (!Launched(view, i))
                return i;
        }
        return null;
    }

    /// <summary>Si un objetivo ya agregó todos sus pasos: la edad, con su clic; los salteados y los que todavía no se generan, siempre.</summary>
    public bool Launched(PlanView view, int index)
    {
        var goal = Plan.Goals[index];
        if (Skipped.ContainsKey(index) || !Generated.Contains(Kind(goal)) || LaunchedGoals.Contains(index))
            return true;
        return goal.Age is { } age && Clicked(view, age);
    }

    /// <summary>Si la edad ya se investigó o tiene su clic en la BO.</summary>
    public bool Clicked(PlanView view, string ageTech) =>
        view.Researched.Contains(ageTech)
        || view.Steps.Any(s => s.Step is { Do: StepAction.Research } && s.Step.What == ageTech)
        || Added.Any(s => s.Do == StepAction.Research && s.What == ageTech);

    private Dictionary<int, string> FindSkipped()
    {
        var skipped = new Dictionary<int, string>();
        var civName = Civ.Names.TryGetValue("mx", out var name) ? name.ToLowerInvariant() : "esta civ";
        var highest = Age.Dark;
        for (var i = 0; i < Plan.Goals.Count; i++)
        {
            var goal = Plan.Goals[i];
            if (goal.Age is { } ageTech)
            {
                if (BuildOrderValidator.AgeOf(ageTech) is { } reached && reached > highest)
                    highest = reached;
                continue;
            }
            var keys = new List<string>();
            var needs = Age.Dark;
            if ((goal.Unit ?? goal.Tech) is { } key && Data.TryGet(key, out var entity))
            {
                if (entity.MadeIn is { } building && Data.TryGet(building, out var made))
                {
                    keys.Add(building);
                    needs = made.Age;
                }
                keys.Add(key);
                needs = entity.Age > needs ? entity.Age : needs;
            }
            else if (goal.TownCenters is not null)
            {
                keys.Add("town_center");
                needs = Data.Get("town_center").Age;
            }
            if (keys.FirstOrDefault(k => !Data.CanMake(Civ.Key, k)) is { } missing)
                skipped[i] = $"Los {civName} no tienen {StepText.Name(missing, missing == goal.Unit ? 2 : 1)}.";
            else if (needs > highest)
                skipped[i] = $"Pedí antes la {StepText.Name(AgeTechs[(int)needs - 1])}.";
        }
        return skipped;
    }

    /// <summary>Cuándo se reacomodaron aldeanos por última vez (ver <see cref="AgeRule"/>).</summary>
    public double? RebalancedAt { get; set; }

    /// <summary>La fuente de comida a la que van los aldeanos nuevos (ovejas, jabalí, bayas, granjas).</summary>
    public GatherSource FoodSource { get; set; } = GatherSource.Sheep;

    /// <summary>Lo que el objetivo activo pide juntar (el costo de la edad que se busca), para el reparto.</summary>
    public ResourceAmounts Planned { get; set; }

    /// <summary>Lo que va a hacer falta para sostener la producción y no está pedido todavía (la madera de las granjas que vienen).</summary>
    public ResourceAmounts Upcoming { get; set; }

    /// <summary>
    /// Lo que va a hacer falta más adelante (la madera de las granjas de todos los que juntan comida salvaje): guía el
    /// reparto de los aldeanos nuevos, pero no aparta stock como <see cref="Upcoming"/>.
    /// </summary>
    public ResourceAmounts Foreseen { get; set; }

    /// <summary>Hasta cuántos aldeanos entran antes del próximo clic de edad (null = sin clic cerca).</summary>
    public int? VillagersBeforeClick { get; set; }

    /// <summary>La hora más cercana a la que alguna regla pidió que la vuelvan a consultar en esta consulta.</summary>
    public double? WakeAt { get; set; }

    public void WakeMeAt(double time) => WakeAt = WakeAt is { } at ? Math.Min(at, time) : time;

    public string NewId() => $"s{++_ids}";

    public Step Add(Step step, string reason)
    {
        Reasons[step.Id] = reason;
        Added.Add(step);
        return step;
    }

    /// <summary>Lo que tarda el centro urbano en sacar un aldeano, con la civ.</summary>
    public double VillagerTime(Age age) => Civ.Time(Data.Get("villager"), age) / Civ.WorkRate("town_center", age);

    /// <summary>
    /// Si alcanza ya para pagar un edificio: el stock menos lo que cuestan los edificios e investigaciones agregados en
    /// esta consulta. El generador agrega un edificio solo cuando alcanza: en el Planner lo que no es aldeanos arranca en
    /// orden, y un edificio esperando madera frenaría detrás un "mover" de comida.
    /// </summary>
    public bool Affordable(PlanView view, ResourceAmounts cost) => Available(view).Covers(cost);

    /// <summary>Cuántos edificios de ese costo alcanzan ya (por la madera; las granjas).</summary>
    public int AffordableCount(PlanView view, ResourceAmounts cost) =>
        cost.Wood <= 0 ? int.MaxValue : (int)Math.Floor(Math.Max(0, Available(view).Wood) / cost.Wood);

    /// <summary>
    /// Lo que se puede gastar ya: el stock menos lo agregado en esta consulta y menos lo que esperan las reglas de más
    /// prioridad (<see cref="Upcoming"/>, que corren antes): así una granja de 60 no se come la madera que junta el
    /// campamento de 100.
    /// </summary>
    private ResourceAmounts Available(PlanView view) => view.Stock - Spent(view) - Upcoming;

    public ResourceAmounts Cost(PlanView view, string key) => Civ.Cost(Data.Get(key), view.Age);

    private ResourceAmounts Spent(PlanView view) => Added
        .Where(s => s.Do is StepAction.Build or StepAction.Research && s.What is { } what && Data.TryGet(what, out _))
        .Aggregate(ResourceAmounts.Zero, (sum, s) => sum + Cost(view, s.What!) * (s.Do == StepAction.Build ? s.Count : 1));

    /// <summary>
    /// Si el centro urbano investiga o tiene una investigación esperando (las edades): mientras, no salen aldeanos. Con
    /// la investigación en curso, <paramref name="until"/> es la hora en que termina; si espera, null.
    /// </summary>
    public bool TownCenterResearching(PlanView view, out double? until)
    {
        until = null;
        var busy = false;
        foreach (var research in view.Steps.Where(s => s.End is null).Select(s => (s.Step, s.Started, s.Start))
                     .Concat(Added.Select(s => (Step: s, Started: false, Start: (double?)null))))
        {
            if (research.Step.Do != StepAction.Research || research.Step.What is not { } what
                || !Data.TryGet(what, out var entity) || entity.MadeIn != "town_center")
                continue;
            busy = true;
            if (research.Started && research.Start is { } start)
                until = start + Civ.Time(entity, view.Age) / Civ.WorkRate("town_center", view.Age);
        }
        return busy;
    }

    /// <summary>Los aldeanos que faltan encolar: los de los pasos de la foto que no salieron y los agregados ahora.</summary>
    public int QueuedVillagers(PlanView view) =>
        view.Steps.Where(s => s.Step is { Do: StepAction.Train, What: "villager" }).Sum(s => s.Remaining)
        + Added.Where(s => s is { Do: StepAction.Train, What: "villager" }).Sum(s => s.Count);

    /// <summary>
    /// Los aldeanos de una fuente que todavía se pueden sacar: los que hay menos los que ya se pidió sacar de ahí (los
    /// "mover" sin hacer, las granjas sin empezar y los constructores que todavía no salieron), de la foto y de esta
    /// consulta. Un "mover" desde una fuente vacía espera para siempre y frena todo lo que va detrás.
    /// </summary>
    public int Free(PlanView view, GatherSource source)
    {
        var free = view.Villagers.GetValueOrDefault(source);
        foreach (var pending in view.Steps.Where(s => s.End is null && s.Step.From == source))
            free -= Taking(pending.Step, pending.Remaining, pending.Started);
        foreach (var added in Added.Where(s => s.From == source))
            free -= Taking(added, added.Count, started: false);
        return Math.Max(0, free);
    }

    private static int Taking(Step step, int remaining, bool started) => step.Do switch
    {
        StepAction.Move => step.Count,
        StepAction.Build when step.What == "farm" => remaining, // cada granja se lleva a su granjero
        StepAction.Build when !started => step.Builders ?? 1,
        _ => 0,
    };

    /// <summary>Quién hace un edificio: un leñador libre; si no hay, de donde haya más gente libre; null, los sin tarea.</summary>
    public GatherSource? Builder(PlanView view) =>
        Free(view, GatherSource.Wood) > 0
            ? GatherSource.Wood
            : view.Villagers.Keys.Select(s => (Source: s, Free: Free(view, s))).Where(x => x.Free > 0)
                .OrderByDescending(x => x.Free).Select(x => (GatherSource?)x.Source).FirstOrDefault();

    /// <summary>La granja de la civ; null si no tiene (los kitanos usan pastos, que game_data no tiene).</summary>
    public string? Farm => CivBuildings.For(Data, Civ, "farm");

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
