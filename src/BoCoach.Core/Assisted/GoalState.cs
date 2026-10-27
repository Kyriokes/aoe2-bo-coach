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

    /// <summary>Los pasos de cada objetivo que no es de edad (por índice), para la hora en que se cumple.</summary>
    public Dictionary<int, List<string>> GoalSteps { get; } = [];

    /// <summary>Anota un paso de un objetivo y, con <paramref name="last"/>, que ya agregó todos.</summary>
    public void Launch(int index, string stepId, bool last = true)
    {
        if (!GoalSteps.TryGetValue(index, out var ids))
            GoalSteps[index] = ids = [];
        ids.Add(stepId);
        if (last)
            LaunchedGoals.Add(index);
    }

    /// <summary>Cuántos edificios de esa clave hay: hechos, en obra y pedidos (en la foto y en esta consulta).</summary>
    public int Count(PlanView view, string key) =>
        view.Buildings.GetValueOrDefault(key) + view.UnderConstruction.GetValueOrDefault(key)
        + view.Steps.Where(s => s.End is null && s.Step is { Do: StepAction.Build } && s.Step.What == key).Sum(s => s.Remaining)
        + Added.Where(s => s.Do == StepAction.Build && s.What == key).Sum(s => s.Count);

    /// <summary>
    /// Los edificios que producen sin parar: los centros urbanos y los que entrenan las unidades de los pasos sin
    /// terminar (para el margen de las casas).
    /// </summary>
    public int Producers(PlanView view) =>
        view.Buildings.GetValueOrDefault("town_center")
        + view.Steps.Where(s => s.End is null && s.Step is { Do: StepAction.Train } && s.Step.What is { } w && w != "villager")
            .Select(s => Data.TryGet(s.Step.What!, out var e) ? e.MadeIn : null).OfType<string>().Distinct()
            .Sum(b => view.Buildings.GetValueOrDefault(b));

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

    /// <summary>Si un objetivo ya agregó todos sus pasos: la edad, con su clic; los salteados, siempre.</summary>
    public bool Launched(PlanView view, int index)
    {
        var goal = Plan.Goals[index];
        if (Skipped.ContainsKey(index) || LaunchedGoals.Contains(index))
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
                    // El centro urbano del arranque ya está (el que se construye es de Castillos).
                    needs = building == BuildOrderValidator.StartingBuilding ? Age.Dark : made.Age;
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

    /// <summary>
    /// Lo que pide la edad que sigue a la del clic de ahora (la madera de sus 2 edificios, su oro y su piedra): mientras
    /// se investiga la edad no salen aldeanos, así que los que salen antes del clic ya van a eso. Guía el reparto
    /// después de lo de ahora, sin apartar stock.
    /// </summary>
    public ResourceAmounts Ahead { get; set; }

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

    /// <summary>Los centros urbanos hechos (al menos el inicial).</summary>
    public int TownCenters(PlanView view) => Math.Max(1, view.Buildings.GetValueOrDefault("town_center"));

    /// <summary>Cada cuánto nace un aldeano con todos los centros urbanos sacando aldeanos.</summary>
    public double VillagerInterval(PlanView view) => VillagerTime(view.Age) / TownCenters(view);

    /// <summary>
    /// Si un centro urbano puede empezar algo ya: alguno sin trabajo y sin aldeanos por encolar (si no, los aldeanos de
    /// la cola lo toman primero). Las unidades de otros edificios no cuentan.
    /// </summary>
    public bool TownCenterFree(PlanView view) =>
        view.Busy.GetValueOrDefault("town_center") < TownCenters(view) && QueuedVillagers(view) == 0;

    /// <summary>
    /// Si una investigación puede arrancar ya (si no, esperaría en la cola y frenaría lo de detrás): la edad alcanzada,
    /// el costo en el stock y su edificio libre (un centro urbano libre y sin aldeanos por encolar, o una instancia del
    /// edificio sin trabajo ni cola).
    /// </summary>
    public bool CanResearchNow(PlanView view, string tech)
    {
        var entity = Data.Get(tech);
        if (entity.Age > view.Age || !Affordable(view, Civ.Cost(entity, view.Age)) || entity.MadeIn is not { } building)
            return false;
        if (building == BuildOrderValidator.StartingBuilding)
            return TownCenterFree(view);
        return view.Buildings.GetValueOrDefault(building) > view.Busy.GetValueOrDefault(building) && view.Queued.GetValueOrDefault(building) == 0;
    }

    /// <summary>
    /// Corta el tramo de aldeanos para que un centro urbano quede libre cuando se junte <paramref name="cost"/> (como para
    /// el clic de edad) y pide que la despierten entonces.
    /// </summary>
    public void HoldTownCenterFor(PlanView view, ResourceAmounts cost)
    {
        var toAfford = AgeRule.SecondsToAfford(view, cost) ?? 0;
        var wait = TownCenterWait(view);
        if (double.IsInfinity(toAfford))
        {
            WakeMeAt(view.Time + 30);
            return;
        }
        var before = Math.Clamp((int)Math.Ceiling((toAfford - wait) / VillagerInterval(view)), 0, VillagerRule.MaxTramo * TownCenters(view));
        VillagersBeforeClick = VillagersBeforeClick is { } other ? Math.Min(other, before) : before;
        WakeMeAt(view.Time + Math.Max(1, Math.Ceiling(Math.Max(toAfford, wait))));
    }

    /// <summary>Cuánto falta para que un centro urbano quede libre (0 si ya está).</summary>
    public double TownCenterWait(PlanView view) =>
        TownCenterFree(view) ? 0 : Math.Max(1, view.Busy.GetValueOrDefault("town_center") - TownCenters(view) + 1 + QueuedVillagers(view)) * VillagerInterval(view);

    /// <summary>
    /// Si alcanza ya para pagar un edificio: el stock menos lo que cuestan los edificios e investigaciones agregados en
    /// esta consulta. El generador agrega un edificio solo cuando alcanza: en el Planner lo que no es aldeanos arranca en
    /// orden, y un edificio esperando madera frenaría detrás un "mover" de comida.
    /// </summary>
    public bool Affordable(PlanView view, ResourceAmounts cost)
    {
        // Solo los recursos que pide: lo reservado de madera (las granjas que vienen) no frena lo que se paga con oro.
        var available = Available(view);
        return Enum.GetValues<Resource>().All(r => cost[r] <= 0 || available[r] >= cost[r]);
    }

    /// <summary>Cuántos edificios de ese costo alcanzan ya (por la madera; las granjas).</summary>
    public int AffordableCount(PlanView view, ResourceAmounts cost) =>
        cost.Wood <= 0 ? int.MaxValue : (int)Math.Floor(Math.Max(0, Available(view).Wood) / cost.Wood);

    /// <summary>
    /// Lo que se puede gastar ya: el stock menos lo agregado en esta consulta y menos lo que esperan las reglas de más
    /// prioridad (<see cref="Upcoming"/>, que corren antes): así una granja de 60 no se come la madera que junta el
    /// campamento de 100.
    /// </summary>
    private ResourceAmounts Available(PlanView view) => view.Stock - Spent(view) - Unpaid(view) - Upcoming;

    /// <summary>
    /// Lo que deben los edificios de la foto que todavía no arrancaron (la segunda casa de la apertura): si no, un
    /// edificio nuevo contaba con esa madera, esperaba y frenaba lo de detrás.
    /// </summary>
    private ResourceAmounts Unpaid(PlanView view) => view.Steps
        .Where(s => s.End is null && s.Remaining > 0 && s.Step is { Do: StepAction.Build, What: { } what } && Data.TryGet(what, out _))
        .Aggregate(ResourceAmounts.Zero, (sum, s) => sum + Cost(view, s.Step.What!) * s.Remaining);

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
        var researching = 0;
        foreach (var research in view.Steps.Where(s => s.End is null).Select(s => (s.Step, s.Started, s.Start))
                     .Concat(Added.Select(s => (Step: s, Started: false, Start: (double?)null))))
        {
            if (research.Step.Do != StepAction.Research || research.Step.What is not { } what
                || !Data.TryGet(what, out var entity) || entity.MadeIn != "town_center")
                continue;
            researching++;
            if (research.Started && research.Start is { } start)
            {
                var end = start + Civ.Time(entity, view.Age) / Civ.WorkRate("town_center", view.Age);
                until = until is { } at ? Math.Min(at, end) : end;
            }
        }
        // Con varios centros urbanos, los que no investigan siguen sacando aldeanos.
        return researching >= TownCenters(view);
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

    /// <summary>
    /// Lo que hay que construir ahora para llegar a <paramref name="building"/>: él mismo, o el primero que falta de lo
    /// que pide antes (la galería de tiro pide el cuartel terminado). Null si lo que pide ya está en obra o pedido: el
    /// paso esperaría delante de los demás y los frenaría.
    /// </summary>
    public string? ToBuild(PlanView view, string building)
    {
        var key = building;
        while (Data.TryGet(key, out var entity) && Civ.Requires(entity) is { } required && view.Buildings.GetValueOrDefault(required) == 0)
        {
            if (Building(view, required) || view.UnderConstruction.GetValueOrDefault(required) > 0)
                return null;
            key = required;
        }
        return key;
    }

    /// <summary>Lo que cuesta <paramref name="building"/> con lo que pide antes y todavía no está ni pedido.</summary>
    public ResourceAmounts CostWithRequirements(PlanView view, string building)
    {
        var cost = Cost(view, building);
        for (var key = building; Data.TryGet(key, out var entity) && Civ.Requires(entity) is { } required && !HasOrWillHave(view, required); key = required)
            cost += Cost(view, required);
        return cost;
    }

    /// <summary>Si ya se pueden poner granjas: la civ las tiene y lo que piden (el molino) está terminado.</summary>
    public bool FarmsReady(PlanView view) => Farm is { } farm && ToBuild(view, farm) == farm;
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
