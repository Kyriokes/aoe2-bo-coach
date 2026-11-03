using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;
using static BoCoach.Core.Lang;

namespace BoCoach.Core.Engine;

/// <summary>Los textos del overlay, armados a partir de un <see cref="CoachView"/>.</summary>
public sealed record CoachLines(
    string Current,
    string? Status,
    string? Next,
    IReadOnlyList<string> Running,
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

    /// <summary>Con la BO terminada (<see cref="CoachView.Compact"/>): el overlay muestra solo <see cref="Card"/>.</summary>
    public bool Compact { get; init; }

    /// <summary>Con <see cref="Compact"/>, lo que pide hacer algo, lo más urgente primero. Vacía: no se muestra nada.</summary>
    public IReadOnlyList<CardLine> Card { get; init; } = [];

    /// <summary>Los renglones fijos de las líneas (<see cref="CoachText.LaneLines"/>).</summary>
    public IReadOnlyList<LaneLine> Lanes { get; init; } = [];

    /// <summary>El overlay muestra las líneas (la BO en curso, sin rama ni abandonada); si no, el título de antes.</summary>
    public bool LanesShown { get; init; }

    /// <summary>
    /// El aviso es un clic de edad del rival: el overlay lo hace titilar entre su color y el del jugador de este número
    /// (1 azul ... 8 naranja; 0 si no se sabe). Null si el aviso es otro.
    /// </summary>
    public int? RivalSlot { get; init; }

    /// <summary>
    /// Lo crítico (spec 2026-11-02, escalón 1): un solo renglón grande arriba de las líneas. La casa que falta (rojo) o
    /// el clic de edad que ya se puede hacer (verde, titilando). Null si no hay nada crítico.
    /// </summary>
    public string? Critical { get; init; }

    /// <summary>De qué es el renglón crítico.</summary>
    public CriticalKind CriticalKind { get; init; }
}

/// <summary>El renglón crítico es algo que hacer ya (verde, titila) o una falta (rojo, fijo).</summary>
public enum CriticalKind
{
    Do,
    Lack,
}

/// <summary>Color de un renglón de línea: se puede hacer ya (blanco), espera recursos o población (amarillo), fallback (gris).</summary>
public enum LaneTone
{
    Now,
    Waiting,
    Fallback,
}

/// <summary>El renglón fijo de una línea en el overlay (spec 2026-10-30): "Investigar · Hacha de doble filo · faltan 40 de madera".</summary>
public sealed record LaneLine(Lane Lane, string Label, string Text, LaneTone Tone);

/// <summary>Un renglón de la tarjeta chica de la BO terminada.</summary>
public sealed record CardLine(string Text, CardKind Kind);

/// <summary>De qué es un renglón de la tarjeta (el overlay le da su color).</summary>
public enum CardKind
{
    /// <summary>Aviso del log o señal de la partida (granja agotada, ataque).</summary>
    Alert,

    /// <summary>El rival hizo clic en una edad: titila con su color.</summary>
    RivalClick,

    /// <summary>El centro urbano parado.</summary>
    TownCenter,

    /// <summary>Se puede hacer clic en la próxima edad.</summary>
    Ready,

    /// <summary>Faltan edificios para la próxima edad.</summary>
    Missing,

    /// <summary>Un edificio de la postura parado o que falta.</summary>
    Building,

    /// <summary>Mover aldeanos de un recurso a otro.</summary>
    Suggestion,

    /// <summary>Una tecnología que falta.</summary>
    Tech,
}

public static class CoachText
{
    /// <summary>
    /// Un renglón por línea, en el orden de <see cref="Lanes.All"/>, solo las que tienen algo que decir: su paso (blanco,
    /// o amarillo con lo que espera) o su fallback en gris (GD, casa, tecnología que falta, edificio de la postura).
    /// La producción militar en marcha va en Producir ("Seguí produciendo arqueros: 3/6"). Sin <paramref name="data"/>
    /// no avisa "sin lugar" (el panel de la app no la tiene).
    /// </summary>
    public static IReadOnlyList<LaneLine> LaneLines(CoachView view, GameData? data)
    {
        var lines = new List<LaneLine>();
        foreach (var lane in view.Lanes)
        {
            var label = LaneLabel(lane.Lane);
            if (lane.Step is { } step)
            {
                var waiting = Waiting(view, lane, data);
                var text = waiting is null ? Goal(step) : $"{Goal(step)} · {waiting}";
                lines.Add(new LaneLine(lane.Lane, label, text, waiting is null ? LaneTone.Now : LaneTone.Waiting));
            }
            else if (Fallback(view, lane.Lane) is { } fallback)
            {
                lines.Add(fallback.Producing
                    ? new LaneLine(lane.Lane, label, fallback.Text, LaneTone.Now)
                    : new LaneLine(lane.Lane, label, fallback.Text, LaneTone.Fallback));
            }
        }
        return lines;
    }

    private static string ProducingPrefix => L("Seguí produciendo", "Keep producing");

    private static string LaneLabel(Lane lane) => lane switch
    {
        Lane.Villagers => L("Aldeanos", "Villagers"),
        Lane.Build => L("Construir", "Build"),
        Lane.Research => L("Investigar", "Research"),
        _ => L("Producir", "Produce"),
    };

    /// <summary>Por qué espera el paso de una línea ("faltan 40 de madera (0:35)", "a 20 de población"), o null si se puede ya.</summary>
    private static string? Waiting(CoachView view, LaneView lane, GameData? data)
    {
        var step = lane.Step!.Step;
        // Antes de su población, lo que falte de recursos no importa todavía (el cuartel a 20 decía "faltan 65 de
        // madera" con 16 de población en la 20261031-145925).
        // Si la población es la del paso (at_pop), el nombre ya la dice ("Edad Feudal a 20 de población"): solo cuánto vas.
        if (lane.Step.AtPop is { } pop && view.Population < pop)
            return step.AtPop == pop ? L($"vas {view.Population}", $"now {view.Population}") : L($"a {pop} de población (vas {view.Population})", $"at {pop} pop (now {view.Population})");
        if (lane.Lane != Lane.Villagers)
        {
            var missing = Enum.GetValues<Resource>().Where(r => lane.Missing[r] >= 1)
                .Select(r => Amount(lane.Missing[r], r)).ToList();
            if (missing.Count > 0)
            {
                var eta = lane.Eta is { } at && at - view.Time >= 5 ? $" ({GameClock.Format(at - view.Time)})" : "";
                return L($"faltan {Join(missing)}{eta}", $"need {Join(missing)}{eta}");
            }
        }
        if (step.AtTime is { } time && view.Time < time)
            return L($"a los {GameClock.Format(time)}", $"at {GameClock.Format(time)}");
        if (step.What is { } what && data is not null && data.TryGet(what, out var entity) && entity.Pop > 0 && view.Population >= view.PopulationCap)
            return L("sin lugar: hacé una casa", "no room: build a house");
        return null;
    }

    private static (string Text, bool Producing)? Fallback(CoachView view, Lane lane)
    {
        switch (lane)
        {
            case Lane.Villagers when view.VillagerGuide is { } resource:
                return (L($"Aldeano → {Suggestion.Name(resource)}, según lo que falta", $"Villager → {Suggestion.Name(resource)}, based on what's short"), false);
            case Lane.Build when view.NeedsHouse:
                return (HouseText(view), false);
            case Lane.Research when view.Techs.Count > 0:
                return (TechText(view.Techs[0]), false);
            case Lane.Produce:
                var producing = view.Running.Where(r => r.Progress.State != StepState.InProgress && r.Progress.Step is { Do: StepAction.Train, What: not null })
                    .GroupBy(r => r.Progress.Step.What!).FirstOrDefault();
                if (producing is not null)
                    return ($"{ProducingPrefix} {StepText.Name(producing.Key, 2)}: {producing.Sum(r => r.Progress.Completed)}/{producing.Sum(r => r.Progress.Required)}", true);
                if (view.Posture?.Gaps.FirstOrDefault() is { } gap)
                    return (GapText(gap), false);
                return null;
            default:
                return null;
        }
    }

    /// <summary>Desvío (en segundos) a partir del cual el objetivo se muestra como atrasado.</summary>
    public const double BehindThreshold = 15;

    /// <param name="backHotkey">El atajo de "Atrás" que eligió el jugador, para el texto de la BO abandonada.</param>
    public static CoachLines Lines(CoachView view, GameData data, string backHotkey = "Ctrl+Alt+←")
    {
        var lines = BaseLines(view, data, backHotkey);
        // D3: una señal reciente (el rival hizo clic, perdiste aldeanos) va en el renglón de avisos, antes que el del log.
        if (view.Signals.Count > 0)
            lines = lines with { Alert = view.Signals[^1].Text, RivalSlot = view.Signals[^1].IsRivalClick ? view.Signals[^1].Slot : null };
        // Una rama de la BO en curso va primero; lo que pedía la BO queda como "después".
        if (view.Branch is { Current: { } step } branch)
        {
            var progress = step.Required > 1 && step.Completed > 0 ? $" ({step.Completed}/{step.Required})" : "";
            lines = lines with
            {
                Current = $"{branch.Title}: {StepText.Describe(step.Step)}{progress}",
                Status = branch.Note,
                Next = L($"Después, la BO: {lines.Current}", $"Then, the BO: {lines.Current}"),
                Goals = [],
            };
        }
        if (view.Compact)
            lines = lines with { Compact = true, Card = Card(view, lines) };
        var lanes = LaneLines(view, data);
        var shown = lanes.Count > 0 && view.Abandoned is null && view.Branch is null && !view.Finished;
        // Con las líneas, la producción en marcha va en el renglón de Producir solo si ese renglón la usa.
        var produce = shown ? lanes.FirstOrDefault(l => l.Lane == Lane.Produce && l.Text.StartsWith(ProducingPrefix, StringComparison.Ordinal)) : null;
        var running = produce is null ? lines.Running : lines.Running.Where(r => r != produce.Text).ToList();
        var critical = Critical(view, data, shown);
        return lines with { Lanes = lanes, LanesShown = shown, Running = running, Critical = critical?.Text, CriticalKind = critical?.Kind ?? CriticalKind.Do };
    }

    /// <summary>
    /// El renglón crítico (spec 2026-11-02, escalón 1): la casa cuando faltan lugares por el ritmo de producción, o el
    /// clic de edad de la BO cuando ya se puede (edificios, recursos y población): en la partida 20261101-230201 la
    /// Feudal estuvo 14 s "posible" con la misma frase en blanco y el jugador no lo notó.
    /// </summary>
    public static (string Text, CriticalKind Kind)? Critical(CoachView view, GameData? data, bool lanesShown)
    {
        if (view.NeedsHouse)
            return (HouseText(view), CriticalKind.Lack);
        if (!lanesShown)
            return null;
        foreach (var lane in view.Lanes)
        {
            if (lane is { Lane: Lane.Research, Step.Step: { Do: StepAction.Research, What: { } what } } && BuildOrderValidator.AgeOf(what) is not null
                && Waiting(view, lane, data) is null)
                return (ClickText(StepText.Name(what)), CriticalKind.Do);
        }
        return null;
    }

    /// <summary>
    /// La tarjeta de la BO terminada: los avisos, el centro urbano parado, la próxima edad si se puede hacer clic o le
    /// faltan edificios, los edificios de la postura parados, mover aldeanos y las tecnologías que faltan. No va lo que
    /// solo informa (lo que falta juntar para la edad, lo que está en curso, el reparto).
    /// </summary>
    private static List<CardLine> Card(CoachView view, CoachLines lines)
    {
        var card = new List<CardLine>();
        if (lines.Alert is { } alert)
            card.Add(new CardLine($"⚠ {alert}", lines.RivalSlot is null ? CardKind.Alert : CardKind.RivalClick));
        if (lines.Idle is { } idle)
            card.Add(new CardLine($"⏱ {idle}", CardKind.TownCenter));
        if (view.Posture is { NextAgeInProgress: false } posture && (posture.NextAgeReady || posture.NextAgeBuildingsMissing > 0)
            && PostureStatus(posture) is { } status)
            card.Add(new CardLine(status, posture.NextAgeReady ? CardKind.Ready : CardKind.Missing));
        if (view.Posture is { } gaps)
            card.AddRange(gaps.Gaps.Select(g => new CardLine(GapText(g), CardKind.Building)));
        if (lines.Suggestion is { } suggestion)
            card.Add(new CardLine($"⇄ {suggestion}", CardKind.Suggestion));
        card.AddRange(view.Techs.Select(t => new CardLine(TechText(t), CardKind.Tech)));
        return card;
    }

    private static CoachLines BaseLines(CoachView view, GameData data, string backHotkey)
    {
        // Las metas en curso del paso mixto se ven en su renglón de meta, no también en "En curso".
        var goalIndexes = view.Goals.Select(g => g.Index).ToHashSet();
        // Producción militar en marcha: un renglón por unidad, sumando las tandas ("12 + 12 arqueros" = 1/24).
        static bool Producing(RunningStep r) => r.Progress.State != StepState.InProgress && r.Progress.Step is { Do: StepAction.Train, What: not null };
        var running = view.Running.Where(r => !goalIndexes.Contains(r.Progress.Index) && !Producing(r)).Select(r => Running(r, view.Time))
            .Concat(view.Running.Where(Producing).GroupBy(r => r.Progress.Step.What!)
                .Select(g => $"{ProducingPrefix} {StepText.Name(g.Key, 2)}: {g.Sum(r => r.Progress.Completed)}/{g.Sum(r => r.Progress.Required)}"))
            // Lo que quedó atrás en una línea (ya se hizo algo posterior): no traba el renglón, va aparte.
            .Concat(view.Lanes.SelectMany(l => l.Behind).Take(2).Select(s => L("Quedó sin hacer: ", "Left undone: ") + StepText.Describe(s.Step)))
            .ToList();
        if (view.Abandoned is { } reason)
        {
            // D1: sin los pasos viejos; queda lo que siempre vale. "Atrás" la retoma.
            return new CoachLines(
                L("BO abandonada: seguí con lo básico", "BO abandoned: stick to the basics"),
                L($"Porque {reason}. Centro urbano produciendo, casas y nadie inactivo. {backHotkey} vuelve a la BO.",
                    $"Because {reason}. Keep the town center producing, build houses, no idle villagers. {backHotkey} goes back to the BO."),
                null,
                running,
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle),
                DistributionText(view.Distribution));
        }
        if (view.Current is null && view.Posture is { } posture)
        {
            // D2: la BO terminó y queda su postura de cierre. Primero se avisa que terminó y que el panel se achica; con
            // Compact, el overlay muestra solo la tarjeta.
            return new CoachLines(
                BoFinished,
                posture.Queue.Count > 0 ? PostureTitle(posture) : null,
                L("En unos segundos el panel se achica: aparece solo cuando hay algo que hacer", "In a few seconds this panel shrinks: it only shows up when there's something to do"),
                [.. posture.Gaps.Select(GapText), .. running],
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
                view.Finished ? BoFinished : L("Lo que sigue espera la próxima edad o un edificio", "What's next waits for the next age or a building"),
                null,
                null,
                running,
                null,
                false,
                view.Suggestion?.ToString(),
                AlertText(view.Alert),
                IdleText(view.TownCenterIdle));
        }

        var title = StepText.Describe(current.Step);
        if (view.SourceVillagers is { } on && current.Step.Assign is { } source)
            title = VillagersOn(source, on.Current, on.Target); // corto y como el contador del HUD
        else if (current.Required > 1 && current.Completed > 0)
            title += $" ({current.Completed}/{current.Required})";

        var deviation = view.Deviations.FirstOrDefault();
        return new CoachLines(
            title,
            Status(view, current, data),
            view.Next is { } next ? L("Después: ", "Next: ") + StepText.Describe(next.Step) : null,
            running,
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
            return (step.What is { } what ? StepText.Name(what) : StepText.Describe(step)) + L(" (en curso)", " (in progress)");
        if (LivePlan.IsVillagerTarget(step))
            return VillagersOn(step.Assign!.Value, step.Count - goal.Remaining, step.Count);
        if (goal.EarlierCount > 0)
            return StepText.Describe(step) + L($" (van {goal.EarlierDone + goal.Completed} de {goal.EarlierCount + goal.Required})", $" ({goal.EarlierDone + goal.Completed} of {goal.EarlierCount + goal.Required} so far)");
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
        ? BoFinished
        : L($"Postura: {Join(posture.Queue.Select(u => StepText.Name(u, 2)).ToList())} siempre en cola", $"Posture: {Join(posture.Queue.Select(u => StepText.Name(u, 2)).ToList())} always queued");

    /// <summary>La próxima edad de la postura: en curso, qué edificios faltan, qué recursos faltan o "Hacé clic".</summary>
    public static string? PostureStatus(PostureView posture)
    {
        if (posture.NextAge is not { } next)
            return null;
        var name = StepText.Name(next);
        if (posture.NextAgeInProgress)
            return name + L(": en curso", ": in progress");
        if (posture.NextAgeBuildingsMissing > 0 && BuildOrderValidator.AgeUpRequirement(next) is { } requirement)
        {
            var count = posture.NextAgeBuildingsMissing;
            return L($"Para {name}: te falta{(count > 1 ? "n" : "")} {count} edificio{(count > 1 ? "s" : "")} de {BuildOrderValidator.AgeName(requirement.Requires)}",
                $"For {name}: you need {count} more {BuildOrderValidator.AgeName(requirement.Requires)} building{(count > 1 ? "s" : "")}");
        }
        var missing = Enum.GetValues<Resource>().Where(r => posture.NextAgeMissing[r] >= 1)
            .Select(r => Amount(posture.NextAgeMissing[r], r)).ToList();
        return missing.Count == 0 ? ClickText(name) : L($"{name}: faltan {Join(missing)} (guardando para la producción)", $"{name}: need {Join(missing)} (saving for production)");
    }

    /// <summary>"Investigá Carretilla (centro urbano)".</summary>
    public static string TechText(TechReminder tech) => L("Investigá ", "Research ") + $"{StepText.Name(tech.Tech)} ({StepText.Name(tech.Building)})";

    /// <summary>"Establo parado: poné exploradores en cola" o "Sin establo: los exploradores no tienen dónde salir".</summary>
    public static string GapText(PostureGap gap) => gap.Missing
        ? L($"Sin {StepText.Name(gap.Building)}: los {StepText.Name(gap.Unit, 2)} no tienen dónde salir",
            $"No {StepText.Name(gap.Building)}: {StepText.Name(gap.Unit, 2)} have nowhere to train")
        : L($"{Capitalized(StepText.Name(gap.Building, gap.Count))}{(gap.Count > 1 ? $" ({gap.Count}) parad{Ending(gap.Building)}s" : $" parad{Ending(gap.Building)}")}: poné {StepText.Name(gap.Unit, 2)} en cola",
            $"{Capitalized(StepText.Name(gap.Building, gap.Count))}{(gap.Count > 1 ? $" ({gap.Count})" : "")} idle: queue {StepText.Name(gap.Unit, 2)}");

    /// <summary>"a" si el nombre del edificio es femenino ("galería de tiro", "herrería"), por la primera palabra; si no, "o".</summary>
    private static string Ending(string building) => StepText.Name(building).Split(' ')[0].EndsWith('a') ? "a" : "o";

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>"Al terminar el paso: madera 4 · comida 6 · oro 0 · piedra 0", en el orden del HUD del juego.</summary>
    public static string NowTargetText(ResourceAmounts t) => L("Ahora: ", "Now: ") + Split(t);

    public static string StepTargetText(ResourceAmounts t) => L("Al terminar el paso: ", "When the step ends: ") + Split(t);

    private static string Split(ResourceAmounts t) =>
        $"{Suggestion.Name(Resource.Wood)} {t.Wood:0} · {Suggestion.Name(Resource.Food)} {t.Food:0} · {Suggestion.Name(Resource.Gold)} {t.Gold:0} · {Suggestion.Name(Resource.Stone)} {t.Stone:0}";

    /// <summary>"Reparto: madera 4 · comida 9 · oro 0 · piedra 0", en el orden del HUD del juego.</summary>
    public static string? DistributionText(ResourceAmounts? d) => d is { } v ? L("Reparto: ", "Split: ") + Split(v) : null;

    /// <summary>"Centro urbano parado hace 12 s" (lo que pasó de más desde que tendría que haber salido otro aldeano).</summary>
    public static string? IdleText(double? seconds) =>
        seconds is { } s ? L("Centro urbano parado hace ", "Town center idle for ") + (s < 60 ? $"{s:0} s" : GameClock.Format(s)) : null;

    /// <summary>Texto de un aviso del log (ver <see cref="Coach.AlertEvents"/>).</summary>
    public static string? AlertText(string? eventId) => eventId switch
    {
        "farm_exhausted" => L("Se agotó una granja: resembrala", "A farm ran out: reseed it"),
        "fish_trap_expired" => L("Se agotó una trampa de peces", "A fish trap ran out"),
        "under_attack" => L("¡Te están atacando!", "You're under attack!"),
        "attacked_by_animals" => L("Un animal ataca a tus aldeanos", "An animal is attacking your villagers"),
        _ => null,
    };

    /// <summary>"En curso: Edad Feudal (termina en 1:30)".</summary>
    private static string Running(RunningStep running, double now)
    {
        var step = running.Progress.Step;
        // Lo que el juego no avisa (granjas): no frena la BO, pero sigue en pantalla hasta que avance un paso posterior.
        if (running.Progress.State is StepState.Pending or StepState.Ready && step.Do == StepAction.Build)
            return L("Hacé también: ", "Also do: ") + StepText.Describe(step);
        var name = step.What is { } what ? StepText.Name(what) : StepText.Describe(step);
        return running.EndsAt is { } end && end > now
            ? L($"En curso: {name} (termina en {GameClock.Format(end - now)})", $"In progress: {name} (ends in {GameClock.Format(end - now)})")
            : L($"En curso: {name}", $"In progress: {name}");
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
            .Select(r => Amount(view.Missing[r], r))
            .ToList();
        var eta = view.CurrentEta is { } at && at - view.Time >= 5 ? L($" (en {GameClock.Format(at - view.Time)})", $" (in {GameClock.Format(at - view.Time)})") : "";

        if (missing.Count > 0)
            return L($"Faltan {Join(missing)}{eta}", $"Need {Join(missing)}{eta}");
        if (current.AtPop is { } pop && view.Population < pop)
            return L($"A {pop} de población (vas {view.Population})", $"At {pop} pop (now {view.Population})");
        if (step.AtTime is { } time && view.Time < time)
            return L($"A los {GameClock.Format(time)}", $"At {GameClock.Format(time)}");
        if (step.What is { } what && data.TryGet(what, out var entity) && entity.Pop > 0 && view.Population >= view.PopulationCap)
            return L("Sin lugar: hacé una casa", "No room: build a house");
        return current.State == StepState.Ready ? L("Ya se puede", "Ready") : null;
    }

    private static string Join(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))}{L(" y ", " and ")}{parts[^1]}";

    private static string BoFinished => L("BO terminada", "BO finished");

    private static string HouseText(CoachView view)
    {
        var left = Math.Max(0, view.PopulationCap - view.Population);
        return L($"Casa: quedan {left} lugares", $"House: {left} {(left == 1 ? "slot" : "slots")} left");
    }

    private static string ClickText(string age) => L($"Hacé clic en {age}", $"Click {age}");

    private static string VillagersOn(GatherSource source, int current, int target) =>
        L($"Aldeanos a {StepText.Source(source)}: {current}/{target}", $"Villagers on {StepText.Source(source)}: {current}/{target}");

    /// <summary>"40 de madera" ("40 wood").</summary>
    private static string Amount(double missing, Resource resource) =>
        L($"{Math.Ceiling(missing):0} de {Suggestion.Name(resource)}", $"{Math.Ceiling(missing):0} {Suggestion.Name(resource)}");
}

/// <summary>Aldeanos de un recurso contra el reparto: verde, rojo o amarillo en el overlay.</summary>
public enum TargetMatch
{
    Ok,
    Short,
    Over,
}
