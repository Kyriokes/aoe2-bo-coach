using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Los campamentos: si ya hay aldeanos en la madera, el oro o la piedra y su campamento no está hecho, en obra ni
/// pedido, lo pide apenas alcanza (antes que las granjas, que si no se llevan la madera). Cuando un tramo estrena un
/// recurso y alcanza, el campamento lo hace el primer aldeano (<see cref="VillagerRule"/>).
/// </summary>
internal sealed class CampRule : IGoalRule
{
    private static readonly (GatherSource Source, string Camp)[] Camps =
        [(GatherSource.Wood, "lumber_camp"), (GatherSource.Gold, "mining_camp"), (GatherSource.Stone, "mining_camp")];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        var steps = new List<Step>();
        foreach (var (source, key) in Camps)
        {
            if (view.Villagers.GetValueOrDefault(source) == 0 || state.HasOrWillHave(view, key)
                || CivBuildings.For(state.Data, state.Civ, key) is not { } camp)
                continue;
            var cost = state.Cost(view, camp);
            if (!state.Affordable(view, cost))
            {
                state.Upcoming += cost;
                continue;
            }
            steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Build, What = camp, From = source, Assign = source },
                $"{char.ToUpperInvariant(StepText.Name(camp)[0])}{StepText.Name(camp)[1..]}: ya hay aldeanos en {StepText.Source(source)}"));
        }
        return steps;
    }
}
