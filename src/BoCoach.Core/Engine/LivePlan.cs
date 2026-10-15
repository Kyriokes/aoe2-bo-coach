using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Diferencia entre lo estimado y el objetivo de la BO. Positivo = atrasado.</summary>
public sealed record TargetDeviation(string What, double Estimated, double Target)
{
    public double Delta => Estimated - Target;

    public override string ToString() =>
        $"{Label(What)} estimada en {Clock(Estimated)}, objetivo {Clock(Target)}";

    public static string Clock(double seconds) => GameClock.Format(seconds);

    private static string Label(string what) => what switch
    {
        "feudal_age" => "Feudal",
        "castle_age" => "Castillos",
        "imperial_age" => "Imperial",
        _ => what,
    };
}

/// <summary>Re-planificación en vivo: lleva el progreso del Tracker y el estado del jugador al Planner.</summary>
public static class LivePlan
{
    /// <summary>Lo que falta de cada paso no terminado ni salteado, en el orden de la BO (los atrasados también).</summary>
    public static IReadOnlyList<PlanItem> Items(Tracker tracker) => tracker.Steps
        .Where(s => s.State is not (StepState.Done or StepState.Skipped))
        .Select(s => new PlanItem(s.Step, s.Remaining))
        .ToList();

    /// <summary>
    /// Punto de partida a partir de lo que se ve en pantalla (tiempo, stock, población) y de lo que el Tracker
    /// da por hecho (aldeanos por recurso según la BO, edificios, edad).
    /// </summary>
    public static PlanStart Start(Tracker tracker)
    {
        var start = PlanStart.Standard;
        var villagers = Enum.GetValues<GatherSource>().ToDictionary(s => s, _ => 0);
        var idle = start.IdleVillagers;
        var buildings = new Dictionary<string, int>(start.Buildings);
        var age = start.Age;

        int Take(GatherSource? from, int wanted)
        {
            if (from is { } s)
            {
                var n = Math.Min(wanted, villagers[s]);
                villagers[s] -= n;
                return n;
            }
            var fromIdle = Math.Min(wanted, idle);
            idle -= fromIdle;
            return fromIdle;
        }

        void Put(GatherSource? to, int count)
        {
            if (to is { } s)
                villagers[s] += count;
            else
                idle += count;
        }

        foreach (var progress in tracker.Steps.Where(s => s.Completed > 0))
        {
            var step = progress.Step;
            switch (step.Do)
            {
                case StepAction.Train when step.What == "villager":
                    Put(step.Assign, progress.Completed);
                    break;
                case StepAction.Build when step.What is not null:
                    buildings[step.What] = buildings.GetValueOrDefault(step.What) + progress.Completed;
                    if (progress.State == StepState.Done && (step.Assign is not null || step.From is null))
                    {
                        var builders = step.What == "farm" ? progress.Completed : step.Builders ?? 1;
                        Put(step.Assign ?? step.From, Take(step.From, builders));
                    }
                    break;
                case StepAction.Research:
                    age = step.What switch
                    {
                        "feudal_age" => Age.Feudal,
                        "castle_age" => Age.Castle,
                        "imperial_age" => Age.Imperial,
                        _ => age,
                    };
                    break;
                case StepAction.Move when step.From is not null:
                    Put(step.Assign, Take(step.From, step.Count));
                    break;
            }
        }

        var state = tracker.State;
        return new PlanStart(state.Time, state.Stock, state.Population, state.PopulationCap, age, villagers, idle, buildings);
    }

    public static PlanResult Replan(Tracker tracker, Planner planner) => planner.Simulate(Items(tracker), Start(tracker));

    /// <summary>Desvíos contra los objetivos de la BO que todavía no se cumplieron.</summary>
    public static IEnumerable<TargetDeviation> Deviations(BuildOrder bo, PlanResult plan)
    {
        if (bo.Targets?.FeudalClickTime is { } feudal && plan.ClickTime("feudal_age") is { } f)
            yield return new TargetDeviation("feudal_age", f, feudal);
        if (bo.Targets?.CastleClickTime is { } castle && plan.ClickTime("castle_age") is { } c)
            yield return new TargetDeviation("castle_age", c, castle);
    }
}
