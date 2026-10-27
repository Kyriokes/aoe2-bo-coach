using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>La apertura: las 2 casas con los aldeanos iniciales y después a las ovejas, o directo a las ovejas.</summary>
internal sealed class OpeningRule : IGoalRule
{
    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (state.OpeningDone)
            return [];
        state.OpeningDone = true;
        var idle = view.IdleVillagers;
        if (idle == 0)
            return [];
        if (state.Plan.Opening.Houses && state.MakesHouses)
        {
            var house = CivBuildings.For(state.Data, state.Civ, "house")!;
            return [state.Add(
                new Step { Id = state.NewId(), Do = StepAction.Build, What = house, Count = 2, Builders = idle, Assign = GatherSource.Sheep },
                $"Apertura: 2 casas con los {idle} aldeanos iniciales, y después a las ovejas")];
        }
        return [state.Add(
            new Step { Id = state.NewId(), Do = StepAction.Move, Count = idle, Assign = GatherSource.Sheep },
            $"Apertura: los {idle} aldeanos iniciales a las ovejas")];
    }
}
