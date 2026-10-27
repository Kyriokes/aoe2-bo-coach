using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las mejoras eco automáticas (spec, sección 3, regla 6; solo con <see cref="AssistedPlan.EcoUpgrades"/>): con los
/// umbrales de <see cref="TechReminders"/>, una por consulta y cuando puede arrancar ya. Con prioridad debajo del
/// objetivo activo: no se compra si la edad que se busca ya tiene su costo juntado o le falta menos que lo que tarda la
/// mejora. La carretilla y la carreta, que frenan al centro urbano, con <see cref="CartVillagers"/> aldeanos o más. El
/// telar va antes del clic de Feudal (lo agrega <see cref="AgeRule"/>).
/// </summary>
internal sealed class EcoRule : IGoalRule
{
    /// <summary>Desde cuántos aldeanos se compran la carretilla y la carreta.</summary>
    public const int CartVillagers = 15;

    private sealed record Candidate(string Tech, GatherSource? On = null, int Villagers = 0);

    private static readonly Candidate[] Candidates =
    [
        new("double_bit_axe", GatherSource.Wood, TechReminders.GatherVillagers),
        new("horse_collar", GatherSource.Farm, TechReminders.FarmVillagers),
        new("gold_mining", GatherSource.Gold, TechReminders.GatherVillagers),
        new("stone_mining", GatherSource.Stone, TechReminders.GatherVillagers),
        new("wheelbarrow", Villagers: CartVillagers),
        new("bow_saw", GatherSource.Wood, TechReminders.GatherVillagers),
        new("heavy_plow", GatherSource.Farm, TechReminders.FarmVillagers),
        new("gold_shaft_mining", GatherSource.Gold, TechReminders.GatherVillagers),
        new("stone_shaft_mining", GatherSource.Stone, TechReminders.GatherVillagers),
        new("hand_cart", Villagers: CartVillagers),
        new("two_man_saw", GatherSource.Wood, TechReminders.GatherVillagers),
        new("crop_rotation", GatherSource.Farm, TechReminders.FarmVillagers),
    ];

    private static readonly string[] AgeTechs = ["feudal_age", "castle_age", "imperial_age"];

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (!state.Plan.EcoUpgrades)
            return [];
        var villagers = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders;
        foreach (var candidate in Candidates)
        {
            if (!state.Data.TryGet(candidate.Tech, out var entity) || !state.Data.CanMake(state.Civ.Key, candidate.Tech) || entity.Age > view.Age
                || view.Researched.Contains(candidate.Tech) || state.Pending(view).Any(s => s.Do == StepAction.Research && s.What == candidate.Tech))
                continue;
            var cost = state.Civ.Cost(entity, view.Age);
            if (cost == ResourceAmounts.Zero) // le sale gratis a la civ (la collera de los francos)
                continue;
            if (candidate.On is { } on && view.Villagers.GetValueOrDefault(on) < candidate.Villagers || villagers < candidate.Villagers)
                continue;
            if (DelaysAge(view, state, state.Civ.Time(entity, view.Age)))
                return [];
            if (state.CanResearchNow(view, candidate.Tech))
            {
                return [state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = candidate.Tech },
                    candidate.On is { } source
                        ? $"{Capitalize(StepText.Name(candidate.Tech))}: hay {view.Villagers.GetValueOrDefault(source)} en {StepText.Source(source)}"
                        : $"{Capitalize(StepText.Name(candidate.Tech))}: ya hay {villagers} aldeanos")];
            }
            if (entity.MadeIn == BuildOrderValidator.StartingBuilding && state.Affordable(view, cost))
            {
                state.HoldTownCenterFor(view, cost);
                return [];
            }
        }
        return [];
    }

    /// <summary>
    /// Si comprar ahora una mejora atrasaría la edad que se busca: su costo ya está juntado (el clic espera), o le falta
    /// menos que lo que tarda la mejora.
    /// </summary>
    private static bool DelaysAge(PlanView view, GoalState state, double researchSeconds)
    {
        if (state.ActiveGoal(view) is not { } index || state.Plan.Goals[index].Age is not { } goalAge)
            return false;
        var tech = AgeTechs.Take(Array.IndexOf(AgeTechs, goalAge) + 1).FirstOrDefault(t => !state.Clicked(view, t));
        if (tech is null)
            return false;
        var toAfford = AgeRule.SecondsToAfford(view, state.Civ.Cost(state.Data.Get(tech), view.Age)) ?? 0;
        return toAfford < researchSeconds;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
