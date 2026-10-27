using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Los aldeanos sin parar, por tramos (spec, sección 3): cuando no quedan aldeanos por encolar, reparte los que salen
/// hasta lo próximo (como mucho 6) por lo que más va a faltar, y escribe un paso por recurso. El campamento de un
/// recurso nuevo lo hace el primer aldeano que va ahí, como paso mixto (como el editor, D8 (7)).
/// </summary>
internal sealed class VillagerRule : IGoalRule
{
    /// <summary>El largo máximo de un tramo.</summary>
    public const int MaxTramo = 6;

    private static readonly Resource[] Resources = [Resource.Food, Resource.Wood, Resource.Gold, Resource.Stone];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        var villagerSteps = view.Steps.Where(s => IsVillagers(s.Step)).ToList();
        if (villagerSteps.Any(s => s.Remaining > 0) || state.Added.Any(IsVillagers) || state.VillagersBeforeClick == 0)
            return [];
        // Mientras investiga una edad no salen aldeanos (spec, sección 3, regla 5): un tramo encolado esperaría al centro
        // urbano y frenaría lo que viene detrás (un "mover" de la caza que se acaba). El tramo se arma cuando termina.
        if (state.TownCenterResearching(view, out var researchEnd))
        {
            state.WakeMeAt(researchEnd ?? view.Time + 5);
            return [];
        }
        var total = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders + villagerSteps.Sum(s => s.Unfinished);
        var room = state.Plan.Villagers.Until - total;
        if (room <= 0)
            return [];

        var maxTramo = MaxTramo * state.TownCenters(view);
        // Que la tanda no pase el tope con las casas que ya vienen: lo que espera población (la granja de un granjero que
        // no puede nacer) frenaría la casa que la destraba. Sin lugar, se espera a la casa.
        var popRoom = PopulationRoom(view, state);
        if (popRoom <= 0)
        {
            state.WakeMeAt(view.Time + 10);
            return [];
        }
        var k = Math.Max(1, Math.Min(Math.Min(Math.Min(maxTramo, room), popRoom), state.VillagersBeforeClick ?? maxTramo));
        // Con varios centros urbanos los aldeanos salen más seguido.
        var villagerTime = state.VillagerInterval(view);
        var horizon = Math.Clamp(k * villagerTime, 60, 180);
        // Primero lo que sostiene la producción; después, lo que pide el objetivo (la prioridad de D2: economía sana primero).
        var sustain = Missing(view, state, horizon, Math.Min(room, (int)(horizon / villagerTime)));
        // Los campamentos que van a hacer falta: el maderero siempre, el minero si el objetivo pide oro o piedra.
        foreach (var (key, needed) in new[] { ("lumber_camp", true), ("mining_camp", state.Planned.Gold > 0 || state.Planned.Stone > 0) })
        {
            if (needed && !state.HasOrWillHave(view, key) && CivBuildings.For(state.Data, state.Civ, key) is { } camp)
                sustain += state.Cost(view, camp);
        }
        var missing = sustain + state.Planned;
        var farms = state.Farm is null ? 0 : state.AffordableCount(view, state.Cost(view, "farm"));

        // El reparto, de a uno: las ovejas hasta lo que pide la apertura y después lo que más falta.
        var order = new List<GatherSource>();
        var counts = new Dictionary<GatherSource, int>();
        var why = new Dictionary<GatherSource, string>();
        var sheep = Heading(view, state, GatherSource.Sheep);
        for (var j = 0; j < k; j++)
        {
            GatherSource source;
            if (state.FoodSource == GatherSource.Sheep && sheep + counts.GetValueOrDefault(GatherSource.Sheep) < state.Plan.Opening.Sheep
                && (view.Food is null || view.Food.Remaining.GetValueOrDefault(GatherSource.Sheep) > 100))
            {
                source = GatherSource.Sheep;
                why.TryAdd(source, $"la apertura pide {state.Plan.Opening.Sheep} en las ovejas");
            }
            else if (missing.Gold > 0 || missing.Stone > 0)
            {
                // El oro y la piedra del costo de la edad van primero: la producción no los usa, así que con lo que la
                // sostiene primero nunca se juntaban (y el clic a N de población esperaba para siempre).
                var resource = missing.Gold / Math.Max(1e-6, Rate(view, GatherSource.Gold)) >= missing.Stone / Math.Max(1e-6, Rate(view, GatherSource.Stone))
                    ? Resource.Gold
                    : Resource.Stone;
                source = SourceFor(resource, state);
                why.TryAdd(source, $"faltan {Math.Ceiling(missing[resource]):0} de {Name(resource)} para el objetivo");
            }
            else if (Resources.Any(res => sustain[res] > 0))
            {
                var resource = Neediest(sustain, view, state);
                source = resource == Resource.Food ? FoodRule.TargetFor(view, state, horizon) : SourceFor(resource, state);
                why.TryAdd(source, $"faltan {Math.Ceiling(sustain[resource]):0} de {Name(resource)} en los próximos {GameClock.Format(horizon)} para seguir produciendo");
            }
            else
            {
                var resource = Neediest(missing, view, state);
                source = resource == Resource.Food ? FoodRule.TargetFor(view, state, horizon) : SourceFor(resource, state);
                why.TryAdd(source, missing[resource] > 0
                    ? $"faltan {Math.Ceiling(missing[resource]):0} de {Name(resource)} para el objetivo"
                    : "no falta nada: a la madera");
            }
            if (source == GatherSource.Farm && counts.GetValueOrDefault(GatherSource.Farm) >= farms)
            {
                // Sin madera para otra granja: no se agrega una que quedaría esperando (y frenaría lo que viene).
                source = GatherSource.Wood;
                why.TryAdd(source, "sin madera para otra granja: a juntarla");
            }
            if (!counts.ContainsKey(source))
                order.Add(source);
            counts[source] = counts.GetValueOrDefault(source) + 1;
            var r = source.Resource();
            var gathered = Rate(view, source) * Math.Max(0, horizon - (j + 1) * villagerTime);
            missing = missing.With(r, missing[r] - gathered);
            sustain = sustain.With(r, sustain[r] - gathered);
            if (source is GatherSource.Sheep or GatherSource.Hunt or GatherSource.Berries && state.Farm is not null)
            {
                // Más adelante va a necesitar su granja: que la madera venga con él.
                var farm = state.Cost(view, "farm");
                missing += farm;
                sustain += farm;
            }
        }

        var steps = new List<Step>();
        var before = 0;
        // Lo que espera a un aldeano nuevo (su campamento, su granja) frena lo que se agrega detrás: si mientras tanto
        // hay que pasar a los de una comida que se acaba, sin campamento (lo hace después CampRule desde los que ya están
        // ahí) y sin granjas para los nuevos (van a la madera, que necesitan las granjas de los que se quedan sin comida).
        // Con unidades en producción la población no dice cuándo nace el aldeano del campamento: sin campamento.
        var campsBlock = view.InProduction > view.Busy.GetValueOrDefault("town_center")
                         || FoodRule.ActsWithin(view, state, (view.InProduction + 1) * villagerTime);
        string? Camp(GatherSource source) =>
            !campsBlock && CampFor(view, state, source) is { } camp && state.Affordable(view, state.Cost(view, camp)) ? camp : null;
        var ordered = order.OrderBy(s => Camp(s) is null ? 1 : 0).ToList();
        if (ordered.IndexOf(GatherSource.Farm) is var farmAt and >= 0
            && FoodRule.ActsWithin(view, state, (view.InProduction + ordered.Take(farmAt).Sum(s => counts[s]) + 1) * villagerTime))
        {
            counts[GatherSource.Wood] = counts.GetValueOrDefault(GatherSource.Wood) + counts[GatherSource.Farm];
            why[GatherSource.Wood] = "las granjas, primero para los de la comida que se acaba: a juntar su madera";
            ordered[farmAt] = GatherSource.Wood;
            ordered = ordered.Distinct().ToList();
        }
        foreach (var source in ordered)
        {
            var n = counts[source];
            if (Camp(source) is { } camp)
            {
                var id = state.NewId();
                var group = $"g{id}";
                steps.Add(state.Add(new Step { Id = id, Do = StepAction.Train, What = "villager", Count = 1, Assign = source, Group = group },
                    $"El primer aldeano a {StepText.Source(source)} hace {StepText.Name(camp)} ({why[source]})"));
                steps.Add(state.Add(
                    new Step { Id = state.NewId(), Do = StepAction.Build, What = camp, From = source, Assign = source, Group = group,
                        AtPop = view.Population + view.InProduction + before + 1 },
                    $"{Capitalize(StepText.Name(camp))}: antes de juntar {StepText.Source(source)}"));
                n--;
                before++;
            }
            if (n > 0 && source == GatherSource.Farm)
            {
                // A las granjas, cada uno con la suya: la hace el granjero que todavía no tiene granja.
                var id = state.NewId();
                var group = $"g{id}";
                steps.Add(state.Add(new Step { Id = id, Do = StepAction.Train, What = "villager", Count = n, Assign = source, Group = group },
                    $"{n} {(n == 1 ? "aldeano" : "aldeanos")} a la granja: {why[source]}"));
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Build, What = "farm", Count = n, From = source, Assign = source, Group = group },
                    $"{n} {(n == 1 ? "granja" : "granjas")}, una por granjero"));
                before += n;
            }
            else if (n > 0)
            {
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Train, What = "villager", Count = n, Assign = source },
                    $"{n} {(n == 1 ? "aldeano" : "aldeanos")} a {StepText.Source(source)}: {why[source]}"));
                before += n;
            }
        }
        return steps;
    }

    private static bool IsVillagers(Step step) => step is { Do: StepAction.Train, What: "villager" };

    /// <summary>
    /// Cuántos aldeanos más entran sin pasar el tope: el tope más lo que dan las casas y centros urbanos en obra o
    /// pedidos, menos la población, lo que está en producción y lo que falta encolar (también las unidades).
    /// </summary>
    private static int PopulationRoom(PlanView view, GoalState state)
    {
        if (view.PopulationCap >= BuildOrderValidator.MaxPopulation)
            return int.MaxValue;
        var coming = 0;
        foreach (var key in new[] { CivBuildings.For(state.Data, state.Civ, "house"), "town_center" }.OfType<string>().Distinct())
        {
            var bonus = state.Civ.PopBonus(state.Data.Get(key), view.Age);
            var pending = view.UnderConstruction.GetValueOrDefault(key)
                          + view.Steps.Where(s => s.End is null && s.Step is { Do: StepAction.Build } && s.Step.What == key).Sum(s => s.Remaining)
                          + state.Added.Where(s => s.Do == StepAction.Build && s.What == key).Sum(s => s.Count);
            coming += bonus * pending;
        }
        int Pop(Step step) => step.What is { } what && state.Data.TryGet(what, out var e) ? e.Pop : 0;
        var queued = view.Steps.Where(s => s.End is null && s.Step.Do == StepAction.Train).Sum(s => s.Remaining * Pop(s.Step))
                     + state.Added.Where(s => s.Do == StepAction.Train).Sum(s => s.Count * Pop(s));
        return Math.Min(BuildOrderValidator.MaxPopulation, view.PopulationCap + coming) - view.Population - view.InProduction - queued;
    }

    private static double Rate(PlanView view, GatherSource source) => view.Rates.GetValueOrDefault(source);

    private static GatherSource SourceFor(Resource resource, GoalState state) => resource switch
    {
        Resource.Food => state.FoodSource,
        Resource.Gold => GatherSource.Gold,
        Resource.Stone => GatherSource.Stone,
        _ => GatherSource.Wood,
    };

    /// <summary>El recurso con más segundos de trabajo faltantes (faltante sobre la tasa de un aldeano); sin faltantes, la madera.</summary>
    private static Resource Neediest(ResourceAmounts missing, PlanView view, GoalState state) =>
        Resources.Where(r => missing[r] > 0)
            .Select(r => (r, seconds: missing[r] / Math.Max(1e-6, Rate(view, SourceFor(r, state)))))
            .OrderByDescending(x => x.seconds)
            .Select(x => (Resource?)x.r)
            .FirstOrDefault() ?? Resource.Wood;

    /// <summary>
    /// Lo que falta juntar en el horizonte para sostener la producción: lo que cuestan los pasos pendientes, lo que va a
    /// hacer falta (<see cref="GoalState.Upcoming"/> y <see cref="GoalState.Foreseen"/>), la comida de los aldeanos que se van a entrenar y la madera de las
    /// casas que van a hacer falta, menos el stock y lo que juntan los aldeanos de ahora. El objetivo va aparte.
    /// </summary>
    private static ResourceAmounts Missing(PlanView view, GoalState state, double horizon, int villagersInHorizon)
    {
        var demand = state.Upcoming + state.Foreseen;
        foreach (var pending in view.Steps.Where(s => s.End is null && s.Remaining > 0 && !IsVillagers(s.Step)))
            demand += Cost(view, state, pending.Step) * InHorizon(view, state, pending, horizon);
        foreach (var added in state.Added.Where(s => !IsVillagers(s)))
            demand += Cost(view, state, added) * (added.Do is StepAction.Train or StepAction.Build ? added.Count : 1);

        var villager = state.Data.Get("villager");
        demand += state.Civ.Cost(villager, view.Age) * villagersInHorizon;
        if (state.MakesHouses && view.PopulationCap < BuildOrderValidator.MaxPopulation)
        {
            var house = state.Data.Get(CivBuildings.For(state.Data, state.Civ, "house")!);
            var over = view.Population + view.InProduction + villagersInHorizon - (view.PopulationCap - HouseRule.Margin);
            var perHouse = Math.Max(1, state.Civ.PopBonus(house, view.Age));
            if (over > 0)
                demand += state.Civ.Cost(house, view.Age) * Math.Ceiling(over / (double)perHouse);
        }

        var income = ResourceAmounts.Zero;
        foreach (var (source, count) in view.Villagers)
            income = income.With(source.Resource(), income[source.Resource()] + count * Rate(view, source) * horizon);
        return demand - view.Stock - income;
    }

    /// <summary>
    /// Cuántas de lo que falta de un paso se pagan en el horizonte: todo, salvo las unidades, que salen de a una por
    /// edificio (spec, sección 3, regla 5: la producción continua es costo dividido tiempo).
    /// </summary>
    private static int InHorizon(PlanView view, GoalState state, PlanViewStep pending, double horizon)
    {
        if (pending.Step is not { Do: StepAction.Train, What: { } unit } || !state.Data.TryGet(unit, out var entity) || entity.MadeIn is not { } building)
            return pending.Remaining;
        var time = state.Civ.Time(entity, view.Age) / state.Civ.WorkRate(building, view.Age);
        var buildings = Math.Max(1, view.Buildings.GetValueOrDefault(building));
        return Math.Min(pending.Remaining, (int)Math.Ceiling(buildings * horizon / time));
    }

    private static ResourceAmounts Cost(PlanView view, GoalState state, Step step) =>
        step.What is { } what && state.Data.TryGet(what, out var entity) ? state.Civ.Cost(entity, view.Age) : ResourceAmounts.Zero;

    /// <summary>Los aldeanos que van a terminar en esa fuente: los que ya están y los que mandan los pasos pendientes.</summary>
    private static int Heading(PlanView view, GoalState state, GatherSource source)
    {
        var heading = view.Villagers.GetValueOrDefault(source);
        foreach (var step in view.Steps.Where(s => s.End is null))
        {
            if (step.Step.Assign != source)
                continue;
            heading += step.Step.Do switch
            {
                StepAction.Train when IsVillagers(step.Step) => step.Remaining + step.Unfinished,
                StepAction.Build when step.Step.From != source => step.Step.Builders ?? 1,
                StepAction.Move => step.Step.Count,
                _ => 0,
            };
        }
        foreach (var step in state.Added.Where(s => s.Assign == source))
        {
            heading += step.Do switch
            {
                StepAction.Train when IsVillagers(step) => step.Count,
                StepAction.Build when step.From != source => step.Builders ?? 1,
                StepAction.Move => step.Count,
                _ => 0,
            };
        }
        return heading;
    }

    /// <summary>El campamento que le falta a un recurso (madera: maderero; oro y piedra: minero), con el de la civ; null si no hace falta.</summary>
    private static string? CampFor(PlanView view, GoalState state, GatherSource source)
    {
        var key = source switch
        {
            GatherSource.Wood => "lumber_camp",
            GatherSource.Gold or GatherSource.Stone => "mining_camp",
            _ => null,
        };
        if (key is null || state.HasOrWillHave(view, key))
            return null;
        return CivBuildings.For(state.Data, state.Civ, key);
    }

    private static string Name(Resource resource) => resource switch
    {
        Resource.Food => "comida",
        Resource.Wood => "madera",
        Resource.Gold => "oro",
        _ => "piedra",
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
