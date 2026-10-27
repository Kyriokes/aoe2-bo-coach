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
        if (villagerSteps.Any(s => s.Remaining > 0) || state.Added.Any(IsVillagers))
            return [];
        var total = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders + villagerSteps.Sum(s => s.Unfinished);
        var room = state.Plan.Villagers.Until - total;
        if (room <= 0)
            return [];

        var k = Math.Max(1, Math.Min(Math.Min(MaxTramo, room), state.VillagersBeforeClick ?? MaxTramo));
        var villagerTime = state.VillagerTime(view.Age);
        var horizon = Math.Clamp(k * villagerTime, 60, 180);
        var missing = Missing(view, state, horizon, Math.Min(room, (int)(horizon / villagerTime)));

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
            else
            {
                var resource = Neediest(missing, view, state);
                source = SourceFor(resource, state);
                why.TryAdd(source, missing[resource] > 0
                    ? $"faltan {Math.Ceiling(missing[resource]):0} de {Name(resource)} en los próximos {GameClock.Format(horizon)}"
                    : "no falta nada: a la madera");
            }
            if (!counts.ContainsKey(source))
                order.Add(source);
            counts[source] = counts.GetValueOrDefault(source) + 1;
            var r = source.Resource();
            missing = missing.With(r, missing[r] - Rate(view, source) * Math.Max(0, horizon - (j + 1) * villagerTime));
        }

        var steps = new List<Step>();
        var before = 0;
        foreach (var source in order)
        {
            var n = counts[source];
            if (CampFor(view, state, source) is { } camp)
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
    /// Lo que falta juntar en el horizonte: lo que cuestan los pasos pendientes, lo que avisan otras reglas
    /// (<see cref="GoalState.Planned"/>), la comida de los aldeanos que se van a entrenar y la madera de las casas que
    /// van a hacer falta, menos el stock y lo que juntan los aldeanos de ahora.
    /// </summary>
    private static ResourceAmounts Missing(PlanView view, GoalState state, double horizon, int villagersInHorizon)
    {
        var demand = state.Planned;
        foreach (var pending in view.Steps.Where(s => s.End is null && s.Remaining > 0 && !IsVillagers(s.Step)))
            demand += Cost(view, state, pending.Step) * pending.Remaining;
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
