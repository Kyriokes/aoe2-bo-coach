using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las unidades (spec, sección 3, regla 3): con el objetivo activo de unidades, construye los N edificios que las producen
/// cuando la edad lo permite y alcanza (de a uno), y pone las unidades en cola apenas hay uno hecho, la edad de la unidad
/// y el costo de la primera: así el paso arranca enseguida y no frena lo de detrás, y los otros edificios se suman al
/// terminar (el Planner produce en paralelo con varios edificios iguales).
/// </summary>
internal sealed class UnitRule : IGoalRule
{
    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (state.ActiveGoal(view) is not { } index || state.Plan.Goals[index] is not { Unit: { } unit } goal)
            return [];
        var entity = state.Data.Get(unit);
        var building = entity.MadeIn!;
        var buildingAge = state.Data.Get(building).Age;
        var count = goal.Count ?? 1;
        var wanted = goal.Buildings ?? 1;
        var unitCost = state.Civ.Cost(entity, view.Age);
        state.Planned += unitCost * count;

        var steps = new List<Step>();
        var have = state.Count(view, building);
        if (have < wanted)
        {
            var cost = state.Cost(view, building);
            if (view.Age >= buildingAge && state.Affordable(view, cost))
            {
                var from = state.Builder(view);
                steps.Add(state.Add(
                    new Step { Id = state.NewId(), Do = StepAction.Build, What = building, From = from, Assign = from ?? GatherSource.Wood },
                    $"{Capitalize(StepText.Name(building))} {have + 1} de {wanted} para {count} {StepText.Name(unit, count)}"));
                have++;
            }
            else if (view.Age >= buildingAge)
            {
                state.Upcoming += cost; // sale cuando alcanza
            }
            else
            {
                state.Planned += cost;
            }
        }

        if (have >= wanted && view.Buildings.GetValueOrDefault(building) > 0 && view.Age >= entity.Age && state.Affordable(view, unitCost))
        {
            var train = state.Add(new Step { Id = state.NewId(), Do = StepAction.Train, What = unit, Count = count },
                $"{count} {StepText.Name(unit, count)} en {wanted} {StepText.Name(building, wanted)}, como pide el objetivo");
            steps.Add(train);
            state.Launch(index, train.Id);
        }
        else if (have >= wanted)
        {
            state.WakeMeAt(view.Time + 15); // el edificio en obra, la edad o el costo de la primera
        }
        return steps;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
