using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Engine;

/// <summary>Los textos del overlay, armados a partir de un <see cref="CoachView"/>.</summary>
public sealed record CoachLines(
    string Current,
    string? Status,
    string? Next,
    IReadOnlyList<string> Running,
    IReadOnlyList<string> Late,
    string? Deviation,
    bool Behind,
    string? Suggestion,
    string? Alert = null,
    string? Idle = null,
    string? Distribution = null)
{
    /// <summary>En un paso largo, cómo repartir los aldeanos de ahora ("Ahora: madera 10 · ..."), arriba del de al terminar.</summary>
    public string? DistributionNow { get; init; }

    /// <summary>Paso mixto: las otras metas del paso actual, una por renglón ("✓" las hechas).</summary>
    public IReadOnlyList<string> Goals { get; init; } = [];
}

public static class CoachText
{
    /// <summary>Desvío (en segundos) a partir del cual el objetivo se muestra como atrasado.</summary>
    public const double BehindThreshold = 15;

    public static CoachLines Lines(CoachView view, GameData data)
    {
        var lines = BaseLines(view, data);
        // D3: una señal reciente (el rival hizo clic, perdiste aldeanos) va en el renglón de avisos, antes que el del log.
        if (view.Signals.Count > 0)
            lines = lines with { Alert = view.Signals[^1].Text };
        // Una rama de la BO en curso va primero; lo que pedía la BO queda como "después".
        if (view.Branch is { Current: { } step } branch)
        {
            var progress = step.Required > 1 && step.Completed > 0 ? $" ({step.Completed}/{step.Required})" : "";
            lines = lines with
            {
                Current = $"{branch.Title}: {StepText.Describe(step.Step)}{progress}",
                Status = branch.Note,
                Next = $"Después, la BO: {lines.Current}",
                Goals = [],
            };
        }
        return lines;
    }

    private static CoachLines BaseLines(CoachView view, GameData data)
    {
        // Las metas en curso del paso mixto se ven en su renglón de meta, no también en "En curso".
        var goalIndexes = view.Goals.Select(g => g.Index).ToHashSet();
        // Producción militar en marcha: un renglón por unidad, sumando las tandas ("12 + 12 arqueros" = 1/24).
        static bool Producing(RunningStep r) => r.Progress.State != StepState.InProgress && r.Progress.Step is { Do: StepAction.Train, What: not null };
        var running = view.Running.Where(r => !goalIndexes.Contains(r.Progress.Index) && !Producing(r)).Select(r => Running(r, view.Time))
            .Concat(view.Running.Where(Producing).GroupBy(r => r.Progress.Step.What!)
                .Select(g => $"Seguí produciendo {StepText.Name(g.Key, 2)}: {g.Sum(r => r.Progress.Completed)}/{g.Sum(r => r.Progress.Required)}"))
            .ToList();
        if (view.Abandoned is { } reason)
        {
            // D1: sin los pasos viejos; queda lo que siempre vale. "Atrás" la retoma.
            return new CoachLines(
                "BO abandonada: seguí con lo básico",
                $"Porque {reason}. Centro urbano produciendo, casas y nadie inactivo. Ctrl+Alt+← vuelve a la BO.",
                null,
                running,
                [],
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle),
                DistributionText(view.Distribution));
        }
        if (view.Current is null && view.Posture is { } posture)
        {
            // D2: la BO terminó y queda su postura de cierre.
            return new CoachLines(
                PostureTitle(posture),
                PostureStatus(posture),
                null,
                [.. posture.Gaps.Select(GapText), .. running],
                [],
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle),
                DistributionText(view.Distribution));
        }
        if (view.Current is not { } current)
        {
            return new CoachLines(
                view.Finished ? "BO terminada" : "Sin pasos pendientes",
                view.Late.Count > 0 ? "Quedan pasos atrasados" : null,
                null,
                running,
                view.Late.Select(s => StepText.Describe(s.Step)).Take(2).ToList(),
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle));
        }

        var title = StepText.Describe(current.Step);
        if (view.SourceVillagers is { } on && current.Step.Assign is { } source)
            title = $"Aldeanos a {StepText.Source(source)}: {on.Current}/{on.Target}"; // corto y como el contador del HUD
        else if (current.Required > 1 && current.Completed > 0)
            title += $" ({current.Completed}/{current.Required})";

        var deviation = view.Deviations.FirstOrDefault();
        return new CoachLines(
            title,
            Status(view, current, data),
            view.Next is { } next ? $"Después: {StepText.Describe(next.Step)}" : null,
            running,
            view.Late.Where(s => !goalIndexes.Contains(s.Index)).Select(s => StepText.Describe(s.Step)).Take(2).ToList(),
            deviation?.ToString(),
            deviation is { Delta: > BehindThreshold },
            view.Suggestion?.ToString(),
            AlertText(view.Alert),
            IdleText(view.TownCenterIdle),
            // La GD (debajo de cada recurso) dice dónde ponerlos ahora; acá, cómo quedan al terminar el paso.
            view.StepTarget is { } target ? StepTargetText(target) : DistributionText(view.Distribution))
        {
            Goals = view.Goals.Where(g => g.Index != current.Index).Select(Goal).ToList(),
            DistributionNow = view.NowTarget is { } now ? NowTargetText(now) : null,
        };
    }

    /// <summary>Una meta de un paso mixto: "✓ Construir 1 cuartel", "Edad Feudal (en curso)", "Aldeanos a madera: 2/6".</summary>
    public static string Goal(StepProgress goal)
    {
        var step = goal.Step;
        if (goal.State is StepState.Done or StepState.Skipped)
            return $"✓ {StepText.Describe(step)}";
        if (goal.State == StepState.InProgress)
            return $"{(step.What is { } what ? StepText.Name(what) : StepText.Describe(step))} (en curso)";
        if (LivePlan.IsVillagerTarget(step))
            return $"Aldeanos a {StepText.Source(step.Assign!.Value)}: {step.Count - goal.Remaining}/{step.Count}";
        return goal.Required > 1 && goal.Completed > 0
            ? $"{StepText.Describe(step)} ({goal.Completed}/{goal.Required})"
            : StepText.Describe(step);
    }

    /// <summary>
    /// Cómo están los aldeanos de un recurso contra lo que pide el reparto: <see cref="TargetMatch.Ok"/> si coinciden,
    /// <see cref="TargetMatch.Short"/> si faltan, <see cref="TargetMatch.Over"/> si sobran, o null si no se sabe cuántos hay.
    /// </summary>
    public static TargetMatch? Match(double target, double? actual) => actual switch
    {
        null => null,
        { } a when Math.Round(a) < Math.Round(target) => TargetMatch.Short,
        { } a when Math.Round(a) > Math.Round(target) => TargetMatch.Over,
        _ => TargetMatch.Ok,
    };

    /// <summary>"Postura: aldeanos y exploradores siempre en cola" (D2).</summary>
    public static string PostureTitle(PostureView posture) => posture.Queue.Count == 0
        ? "BO terminada"
        : $"Postura: {Join(posture.Queue.Select(u => StepText.Name(u, 2)).ToList())} siempre en cola";

    /// <summary>La próxima edad de la postura: en curso, qué edificios faltan, qué recursos faltan o "Hacé clic".</summary>
    public static string? PostureStatus(PostureView posture)
    {
        if (posture.NextAge is not { } next)
            return null;
        var name = StepText.Name(next);
        if (posture.NextAgeInProgress)
            return $"{name}: en curso";
        if (posture.NextAgeBuildingsMissing > 0 && BuildOrderValidator.AgeUpRequirement(next) is { } requirement)
        {
            var count = posture.NextAgeBuildingsMissing;
            return $"Para {name}: te falta{(count > 1 ? "n" : "")} {count} edificio{(count > 1 ? "s" : "")} de {BuildOrderValidator.AgeName(requirement.Requires)}";
        }
        var missing = Enum.GetValues<Resource>().Where(r => posture.NextAgeMissing[r] >= 1)
            .Select(r => $"{Math.Ceiling(posture.NextAgeMissing[r]):0} de {Suggestion.Name(r)}").ToList();
        return missing.Count == 0 ? $"Hacé clic en {name}" : $"{name}: faltan {Join(missing)} (guardando para la producción)";
    }

    /// <summary>"Establo parado: poné exploradores en cola" o "Sin establo: los exploradores no tienen dónde salir".</summary>
    public static string GapText(PostureGap gap) => gap.Missing
        ? $"Sin {StepText.Name(gap.Building)}: los {StepText.Name(gap.Unit, 2)} no tienen dónde salir"
        : $"{Capitalized(StepText.Name(gap.Building, gap.Count))}{(gap.Count > 1 ? $" ({gap.Count}) parados" : " parado")}: poné {StepText.Name(gap.Unit, 2)} en cola";

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>"Al terminar el paso: madera 4 · comida 6 · oro 0 · piedra 0", en el orden del HUD del juego.</summary>
    public static string NowTargetText(ResourceAmounts t) =>
        $"Ahora: madera {t.Wood:0} · comida {t.Food:0} · oro {t.Gold:0} · piedra {t.Stone:0}";

    public static string StepTargetText(ResourceAmounts t) =>
        $"Al terminar el paso: madera {t.Wood:0} · comida {t.Food:0} · oro {t.Gold:0} · piedra {t.Stone:0}";

    /// <summary>"Reparto: madera 4 · comida 9 · oro 0 · piedra 0", en el orden del HUD del juego.</summary>
    public static string? DistributionText(ResourceAmounts? d) => d is { } v
        ? $"Reparto: madera {v.Wood:0} · comida {v.Food:0} · oro {v.Gold:0} · piedra {v.Stone:0}"
        : null;

    /// <summary>"Centro urbano parado hace 12 s" (lo que pasó de más desde que tendría que haber salido otro aldeano).</summary>
    public static string? IdleText(double? seconds) =>
        seconds is { } s ? $"Centro urbano parado hace {(s < 60 ? $"{s:0} s" : GameClock.Format(s))}" : null;

    /// <summary>Texto de un aviso del log (ver <see cref="Coach.AlertEvents"/>).</summary>
    public static string? AlertText(string? eventId) => eventId switch
    {
        "farm_exhausted" => "Se agotó una granja: resembrala",
        "fish_trap_expired" => "Se agotó una trampa de peces",
        "under_attack" => "¡Te están atacando!",
        "attacked_by_animals" => "Un animal ataca a tus aldeanos",
        _ => null,
    };

    /// <summary>"En curso: Edad Feudal (termina en 1:30)".</summary>
    private static string Running(RunningStep running, double now)
    {
        var step = running.Progress.Step;
        // Lo que el juego no avisa (granjas): no frena la BO, pero sigue en pantalla hasta que avance un paso posterior.
        if (running.Progress.State is StepState.Pending or StepState.Ready && step.Do == StepAction.Build)
            return $"Hacé también: {StepText.Describe(step)}";
        var name = step.What is { } what ? StepText.Name(what) : StepText.Describe(step);
        return running.EndsAt is { } end && end > now
            ? $"En curso: {name} (termina en {GameClock.Format(end - now)})"
            : $"En curso: {name}";
    }

    /// <summary>
    /// Por qué todavía no se puede hacer el paso actual, o cuándo se va a poder. En los pasos de aldeanos no se
    /// muestra lo que falta: el centro urbano los va encolando de a uno y el "faltan 50" cambiaría todo el tiempo.
    /// </summary>
    private static string? Status(CoachView view, StepProgress current, GameData data)
    {
        var step = current.Step;
        var villagers = step.Do == StepAction.Train && step.What == "villager";
        var missing = villagers ? [] : Enum.GetValues<Resource>()
            .Where(r => view.Missing[r] >= 1)
            .Select(r => $"{Math.Ceiling(view.Missing[r]):0} de {Suggestion.Name(r)}")
            .ToList();
        var eta = view.CurrentEta is { } at && at - view.Time >= 5 ? $" (en {GameClock.Format(at - view.Time)})" : "";

        if (missing.Count > 0)
            return $"Faltan {Join(missing)}{eta}";
        if (step.AtPop is { } pop && view.Population < pop)
            return $"A {pop} de población (vas {view.Population})";
        if (step.AtTime is { } time && view.Time < time)
            return $"A los {GameClock.Format(time)}";
        if (step.What is { } what && data.TryGet(what, out var entity) && entity.Pop > 0 && view.Population >= view.PopulationCap)
            return "Sin lugar: hacé una casa";
        return current.State == StepState.Ready ? "Ya se puede" : null;
    }

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} y {parts[^1]}";
}

/// <summary>Aldeanos de un recurso contra el reparto: verde, rojo o amarillo en el overlay.</summary>
public enum TargetMatch
{
    Ok,
    Short,
    Over,
}
