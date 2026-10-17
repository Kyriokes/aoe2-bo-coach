using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Diferencia entre lo estimado y el objetivo de la BO. Positivo = atrasado.</summary>
/// <param name="Clicked">El clic ya se hizo: <paramref name="Estimated"/> es el momento real, no una estimación.</param>
public sealed record TargetDeviation(string What, double Estimated, double Target, bool Clicked = false)
{
    public double Delta => Estimated - Target;

    public override string ToString() => Clicked
        ? $"Clic de {Label(What)} a las {Clock(Estimated)}, objetivo {Clock(Target)}"
        : $"{Label(What)} estimada en {Clock(Estimated)}, objetivo {Clock(Target)}";

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
    /// <summary>
    /// Lo que falta de cada paso no terminado ni salteado, en el orden de la BO (los atrasados también). Lo que
    /// está en curso va con el momento en que se pagó.
    /// </summary>
    public static IReadOnlyList<PlanItem> Items(Tracker tracker) => tracker.Steps
        .Where(s => s.State is not (StepState.Done or StepState.Skipped))
        .Select(s => new PlanItem(s.Step, s.Remaining, s.StartedAt))
        .ToList();

    /// <summary>
    /// Punto de partida a partir de lo que se ve en pantalla (tiempo, stock, población) y de lo que el Tracker
    /// da por hecho (aldeanos por recurso según la BO, edificios, edad).
    /// </summary>
    public static PlanStart Start(Tracker tracker)
    {
        var layout = Layout(tracker.Steps.Where(s => s.Completed > 0).Select(s => (s.Step, s.Completed, s.State == StepState.Done)));

        // También las que el log mostró fuera de la BO (una carretilla que no estaba en el plan).
        var state = tracker.State;
        var researched = layout.Researched;
        foreach (var e in state.Events)
        {
            if (EventConventions.Parse(e.EventId) is { Action: StepAction.Research } done && !researched.Contains(done.What))
                researched.Add(done.What);
        }
        return new PlanStart(state.Time, state.Stock, state.Population, state.PopulationCap, layout.Age, layout.Villagers, layout.Idle, layout.Buildings)
        {
            Researched = researched,
        };
    }

    /// <summary>
    /// Dónde quiere la BO a los aldeanos en el punto en que va el jugador: todos los pasos anteriores al actual como
    /// hechos (lo que planea la BO, no lo que el Tracker les asignó) más lo que avanzó el actual. A diferencia de
    /// <see cref="Start"/>, no salta cuando el Tracker reparte los aldeanos nuevos entre varios pasos abiertos.
    /// </summary>
    public static IReadOnlyDictionary<GatherSource, int> IntendedVillagers(Tracker tracker)
    {
        var current = tracker.Current;
        var upTo = current?.Index ?? tracker.Steps.Count;
        var steps = tracker.Steps.Take(upTo).Select(s => (s.Step, s.Required, true)).ToList();
        if (current is not null)
            steps.Add((current.Step, current.Completed, false));
        return Layout(steps).Villagers;
    }

    /// <summary>
    /// Un paso de aldeanos con fuente: su 'count' es cuántos tiene que haber en esa fuente al terminarlo, contando los
    /// que ya estaban (como el contador del HUD), no cuántos se entrenan.
    /// </summary>
    public static bool IsVillagerTarget(Step step) => step is { Do: StepAction.Train, What: "villager", Assign: not null };

    /// <summary>
    /// Cuántas unidades hace cada paso: en los de aldeanos con fuente, lo que falta para llegar a 'count' en esa
    /// fuente ("6 aldeanos en ovejas" con los 3 iniciales ya ahí son 3 nuevos); en los demás, 'count' (o 1).
    /// </summary>
    public static int[] Units(BuildOrder bo)
    {
        var units = new int[bo.Steps.Count];
        for (var i = 0; i < units.Length; i++)
        {
            var step = bo.Steps[i];
            units[i] = IsVillagerTarget(step)
                ? Math.Max(0, step.Count - LayoutOf(bo, units, i).Villagers.GetValueOrDefault(step.Assign!.Value))
                : step.Do is StepAction.Train or StepAction.Build ? step.Count : 1;
        }
        return units;
    }

    /// <summary>
    /// Aldeanos que la BO tiene en cada fuente cuando termina el paso <paramref name="index"/> (todo lo anterior hecho),
    /// contando los 3 iniciales: lo que mostraría el contador del HUD.
    /// </summary>
    public static IReadOnlyDictionary<GatherSource, int> PlannedVillagers(BuildOrder bo, int index) =>
        LayoutOf(bo, Units(bo), index + 1).Villagers;

    /// <summary>
    /// Migración del formato 1, donde 'count' de un paso de aldeanos era cuántos se entrenan: lo pasa al total en la
    /// fuente, así la BO simula lo mismo ("6 nuevos a ovejas" después de las casas son 9 en ovejas).
    /// </summary>
    public static void CountsToTotals(BuildOrder bo)
    {
        var units = bo.Steps.Select(s => s.Do is StepAction.Train or StepAction.Build ? s.Count : 1).ToArray();
        var totals = bo.Steps.Select((s, i) => IsVillagerTarget(s) ? LayoutOf(bo, units, i + 1).Villagers.GetValueOrDefault(s.Assign!.Value) : s.Count).ToList();
        for (var i = 0; i < totals.Count; i++)
            bo.Steps[i].Count = totals[i];
    }

    private static VillagerLayout LayoutOf(BuildOrder bo, int[] units, int stepCount) =>
        Layout(bo.Steps.Take(stepCount).Select((s, j) => (s, units[j], true)));

    private sealed record VillagerLayout(
        Dictionary<GatherSource, int> Villagers, int Idle, Dictionary<string, int> Buildings, Age Age, List<string> Researched);

    /// <summary>Dónde quedan los aldeanos, los edificios y la edad después de hacer esos pasos (en orden).</summary>
    private static VillagerLayout Layout(IEnumerable<(Step Step, int Completed, bool Done)> steps)
    {
        var start = PlanStart.Standard;
        var villagers = Enum.GetValues<GatherSource>().ToDictionary(s => s, _ => 0);
        var idle = start.IdleVillagers;
        var buildings = new Dictionary<string, int>(start.Buildings);
        var age = start.Age;
        var researched = new List<string>();

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

        foreach (var (step, completed, done) in steps)
        {
            switch (step.Do)
            {
                case StepAction.Train when step.What == "villager":
                    Put(step.Assign, completed);
                    break;
                case StepAction.Build when step.What is not null:
                    buildings[step.What] = buildings.GetValueOrDefault(step.What) + completed;
                    if (done && (step.Assign is not null || step.From is null))
                    {
                        var builders = step.What == "farm" ? completed : step.Builders ?? 1;
                        Put(step.Assign ?? step.From, Take(step.From, builders));
                    }
                    break;
                case StepAction.Research:
                    if (done && step.What is not null)
                        researched.Add(step.What);
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
        return new VillagerLayout(villagers, idle, buildings, age, researched);
    }

    public static PlanResult Replan(Tracker tracker, Planner planner) =>
        planner.Simulate(Items(tracker), Start(tracker), planner.CivOf(tracker.BuildOrder));

    /// <summary>Desvíos contra los objetivos de la BO que todavía no se cumplieron.</summary>
    public static IEnumerable<TargetDeviation> Deviations(BuildOrder bo, PlanResult plan)
    {
        if (bo.Targets?.FeudalClickTime is { } feudal && Click(plan, "feudal_age") is { } f)
            yield return new TargetDeviation("feudal_age", f.Time, feudal, f.Paid);
        if (bo.Targets?.CastleClickTime is { } castle && Click(plan, "castle_age") is { } c)
            yield return new TargetDeviation("castle_age", c.Time, castle, c.Paid);
    }

    private static (double Time, bool Paid)? Click(PlanResult plan, string tech)
    {
        var estimate = plan.Steps.FirstOrDefault(s => s.Step.Do == StepAction.Research && s.Step.What == tech);
        return estimate?.Start is { } start ? (start, estimate.Paid) : null;
    }
}
