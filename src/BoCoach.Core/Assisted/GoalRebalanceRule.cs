using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// El reacomodo para los objetivos que no son edades (las edades tienen el suyo en <see cref="AgeRule"/>): ya no salen
/// aldeanos (se llegó a 'until'), así que los nuevos no pueden ir a lo que falta. Con lo que piden las reglas en esta
/// consulta (<see cref="GoalState.Planned"/> y <see cref="GoalState.Upcoming"/>), si un recurso no se junta en
/// <see cref="AgeRule.RebalanceSeconds"/>, pasa aldeanos libres desde lo que sobra (a la comida: a la fuente de ahora o
/// a granjas, una cada uno). Mientras haya un objetivo así, pide que la vuelvan a consultar: sin nacimientos nadie lo
/// hacía y la simulación terminaba con el objetivo sin cumplir.
/// </summary>
internal sealed class GoalRebalanceRule : IGoalRule
{
    private static readonly GatherSource[] Donors =
        [GatherSource.Berries, GatherSource.Hunt, GatherSource.Sheep, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (state.ActiveGoal(view) is { } index && state.Plan.Goals[index].Age is not null)
            return [];
        var villagers = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders
                        + view.Steps.Where(s => s.Step is { Do: StepAction.Train, What: "villager" }).Sum(s => s.Remaining + s.Unfinished);
        if (villagers < state.Plan.Villagers.Until)
            return [];
        const double seconds = AgeRule.RebalanceSeconds;
        // Lo que piden las reglas ahora, más las unidades en cola de objetivos ya arrancados (lo que salen en el horizonte).
        var need = state.Planned + state.Upcoming;
        foreach (var train in view.Steps.Where(s => s.End is null && s.Remaining > 0 && s.Step is { Do: StepAction.Train, What: { } w } && w != "villager"))
        {
            var entity = state.Data.Get(train.Step.What!);
            var time = state.Civ.Time(entity, view.Age) / state.Civ.WorkRate(entity.MadeIn!, view.Age);
            var buildings = Math.Max(1, view.Buildings.GetValueOrDefault(entity.MadeIn!));
            need += state.Civ.Cost(entity, view.Age) * Math.Min(train.Remaining, (int)Math.Ceiling(buildings * seconds / time));
        }
        if (need == ResourceAmounts.Zero)
            return [];
        state.WakeMeAt(view.Time + AgeRule.RebalanceGapSeconds);
        if (view.Time - state.RebalancedAt < AgeRule.RebalanceGapSeconds || view.Steps.Any(s => s.End is null && s.Step.Do == StepAction.Move))
            return [];

        var gathered = AgeRule.Gathered(view, seconds);
        var fromWood = view.Villagers.GetValueOrDefault(GatherSource.Wood) / 2;
        var farmCost = state.Cost(view, "farm");
        var steps = new List<Step>();
        foreach (var resource in new[] { Resource.Food, Resource.Wood, Resource.Gold, Resource.Stone })
        {
            var target = resource switch
            {
                Resource.Food => state.FoodSource,
                Resource.Wood => GatherSource.Wood,
                Resource.Gold => GatherSource.Gold,
                _ => GatherSource.Stone,
            };
            var rate = view.Rates.GetValueOrDefault(target);
            var missing = need[resource] - view.Stock[resource] - gathered[resource];
            if (missing <= 0 || rate <= 0 || target == GatherSource.Farm && state.Farm is null)
                continue;
            var wanted = (int)Math.Ceiling(missing / (rate * seconds));
            foreach (var donor in Donors)
            {
                var donorRate = view.Rates.GetValueOrDefault(donor);
                var surplus = view.Stock[donor.Resource()] + gathered[donor.Resource()] - need[donor.Resource()];
                if (donor.Resource() == resource || wanted <= 0 || surplus <= 0 || donorRate <= 0)
                    continue;
                var moved = Math.Min(Math.Min(wanted, state.Free(view, donor)), (int)Math.Floor(surplus / (donorRate * seconds)));
                if (donor == GatherSource.Wood)
                    moved = Math.Min(moved, fromWood);
                if (target == GatherSource.Farm)
                    moved = Math.Min(moved, state.AffordableCount(view, farmCost));
                if (moved <= 0)
                    continue;
                var why = $"A {StepText.Source(target)}: faltan {Math.Ceiling(need[resource] - view.Stock[resource]):0} de {Name(resource)} para el objetivo y ya no salen aldeanos";
                steps.Add(state.Add(target == GatherSource.Farm
                    ? new Step { Id = state.NewId(), Do = StepAction.Build, What = "farm", Count = moved, From = donor, Assign = GatherSource.Farm }
                    : new Step { Id = state.NewId(), Do = StepAction.Move, Count = moved, From = donor, Assign = target }, why));
                wanted -= moved;
                if (donor == GatherSource.Wood)
                    fromWood -= moved;
                gathered = gathered.With(donor.Resource(), gathered[donor.Resource()] - moved * donorRate * seconds)
                    .With(resource, gathered[resource] + moved * rate * seconds);
            }
        }
        if (steps.Count > 0)
            state.RebalancedAt = view.Time;
        return steps;
    }

    private static string Name(Resource resource) => resource switch
    {
        Resource.Food => "comida",
        Resource.Wood => "madera",
        Resource.Gold => "oro",
        _ => "piedra",
    };
}
