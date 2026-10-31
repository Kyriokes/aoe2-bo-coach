using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

public enum StepState
{
    /// <summary>Todavía no se hizo (puede tener progreso parcial, por ejemplo 3 de 6 aldeanos).</summary>
    Pending,

    /// <summary>Pendiente y ya se puede hacer: hay recursos, población y se cumplen las condiciones.</summary>
    Ready,

    Done,

    /// <summary>Ya se pagó y se está haciendo (por ejemplo, la investigación de una edad), pero no terminó.</summary>
    InProgress,

    /// <summary>El jugador lo salteó a mano.</summary>
    Skipped,
}

public sealed record StepProgress(
    Step Step,
    int Index,
    StepState State,
    int Completed,
    int Required,
    double? DoneAt,
    bool Manual,
    double? StartedAt = null)
{
    public int Remaining => Math.Max(0, Required - Completed);
}

/// <summary>
/// Una acción (unidad, edificio o tecnología) que no corresponde a ningún paso pendiente: suma al gasto pero no mueve
/// el puntero. Los avisos del log ("Farm Exhausted", "Sheep Found") no son acciones y no cuentan.
/// </summary>
public sealed record ExtraAction(GameEvent Event);

/// <summary>
/// Sigue en qué paso de la BO está el jugador. Se recalcula entero en cada <see cref="Update"/> a partir de
/// los eventos, las correcciones manuales y el estado del jugador, así que el resultado no depende del orden
/// en que se llamó.
/// <para>Cada paso cae en una línea (<see cref="Lanes"/>) y cada línea avanza sola: lo que se salteó queda primero en
/// su línea (<see cref="LaneStep"/>) en vez de "atrasado" (spec 2026-10-30, coach por líneas paralelas).</para>
/// </summary>
public sealed class Tracker
{
    private readonly BuildOrder _bo;
    private readonly GameData _data;
    private readonly EventCatalog? _events;
    private readonly string?[] _confirmEvent;
    private readonly Dictionary<int, double> _manualDone = [];
    private readonly Dictionary<int, double> _skipped = [];
    private readonly Dictionary<int, double> _started = [];
    private readonly HashSet<int> _noImplicit = [];
    private readonly HashSet<GameEvent> _ignored = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, List<GameEvent>> _allocated = [];
    private PlayerState _state = new();
    private StepProgress[] _steps = [];
    private readonly int[] _units;
    private List<ExtraAction> _extras = [];

    /// <summary>
    /// Pasos de aldeanos con fuente: el recurso y cuántos tiene que haber ahí al terminar el paso según la BO (lo que
    /// mostraría el HUD). Con los aldeanos por recurso del HUD, el paso cuenta los que ya hay, no los que se vieron salir.
    /// </summary>
    private readonly (Resource Resource, int Total)?[] _hudTarget;

    /// <summary>Segundos que el HUD tiene que mostrar el total para dar el paso por hecho (una lectura mala no alcanza).</summary>
    public const double HudConfirmSeconds = 2;

    private readonly Dictionary<int, double> _hudSince = [];
    private readonly Dictionary<int, double> _hudDone = [];
    private readonly HashSet<int> _noHud = [];

    /// <param name="events">Eventos que Vision sabe reconocer. Un paso cuyo evento no está acá no se puede
    /// confirmar por el log y se da por hecho cuando avanza un paso posterior. Null = todos reconocibles.</param>
    public Tracker(BuildOrder bo, GameData data, EventCatalog? events = null)
    {
        _bo = bo;
        _data = data;
        _events = events;
        _confirmEvent = bo.Steps.Select(ConfirmableEvent).ToArray();
        _units = LivePlan.Units(bo);
        _hudTarget = HudTargets(bo);
        _capTarget = CapTargets(bo, data);
        Recompute();
    }

    /// <summary>
    /// Para cada paso que construye algo que da población (casas): el tope que la BO espera al terminarlo. Si el tope
    /// del juego ya llegó, el paso está hecho aunque el log no lo haya avisado (un "House Built" repetido no agrega
    /// renglón) o las casas se hayan hecho antes: el usuario tenía 35/55 y el coach seguía pidiendo una casa. Solo las
    /// casas (o lo que cuenta como casa): los centros urbanos los avisa el log, y en la 20261030-203917 las casas de más
    /// daban por hechos a las 15:01, en Feudal, los 2 centros urbanos del final de la BO.
    /// </summary>
    private static int?[] CapTargets(BuildOrder bo, GameData data)
    {
        var civ = data.Civ(bo.Civ);
        var cap = PlanStart.For(civ, data).PopulationCap;
        var targets = new int?[bo.Steps.Count];
        for (var i = 0; i < bo.Steps.Count; i++)
        {
            var step = bo.Steps[i];
            if (step.Do != StepAction.Build || step.What is not { } what || !data.TryGet(what, out var entity) || civ.PopBonus(entity, Age.Dark) <= 0)
                continue;
            cap = Math.Min(BuildOrderValidator.MaxPopulation, cap + civ.PopBonus(entity, Age.Dark) * step.Count);
            if (what == "house" || entity.CountsAs.Contains("house"))
                targets[i] = cap;
        }
        return targets;
    }

    private readonly int?[] _capTarget;

    /// <summary>
    /// Para cada paso de aldeanos: el total que la BO tiene en su recurso al terminarlo, solo si es más que lo que tuvo
    /// en cualquier paso anterior. Si la BO antes sacó gente de ese recurso, el HUD ya muestra ese total desde mucho
    /// antes: en la 20261028-232628 (británicos mejorada), "5 en bayas" pedía 6 en comida después de mover 6 de las
    /// ovejas y la caza, y a las 0:52 lo daba por hecho, y con él a todos los "Mover" de antes. Esos pasos se cuentan
    /// por el log, como sin el HUD.
    /// </summary>
    private static (Resource Resource, int Total)?[] HudTargets(BuildOrder bo)
    {
        var targets = new (Resource, int)?[bo.Steps.Count];
        var peak = new Dictionary<Resource, int>();
        for (var i = 0; i < bo.Steps.Count; i++)
        {
            var totals = LivePlan.PlannedVillagers(bo, i).GroupBy(v => v.Key.Resource()).ToDictionary(g => g.Key, g => g.Sum(v => v.Value));
            if (LivePlan.IsVillagerTarget(bo.Steps[i]) && bo.Steps[i].Assign!.Value.Resource() is var resource
                && totals.GetValueOrDefault(resource) is var total && total > peak.GetValueOrDefault(resource))
                targets[i] = (resource, total);
            foreach (var (r, t) in totals)
                peak[r] = Math.Max(peak.GetValueOrDefault(r), t);
        }
        return targets;
    }

    public BuildOrder BuildOrder => _bo;
    public IReadOnlyList<StepProgress> Steps => _steps;
    public IReadOnlyList<ExtraAction> Extras => _extras;
    public PlayerState State => _state;

    /// <summary>
    /// Paso a mostrar en grande: el primero pendiente que no quedó atrasado ni es producción militar ya empezada.
    /// </summary>
    public StepProgress? Current => _steps.FirstOrDefault(s => IsOpen(s) && !IsProducing(s) && !IsSilent(s) && _eligible[s.Index]);

    /// <summary>
    /// El renglón de una línea: su primer paso abierto, en el orden de la BO, que es elegible (<see cref="IsEligible"/>)
    /// y no es producción en marcha ni una granja suelta. Null si la línea no tiene ninguno (el coach usa el fallback).
    /// </summary>
    public StepProgress? LaneStep(Lane lane)
    {
        var shown = _steps.Where(s => Lanes.Of(s.Step) == lane && Shown(s)).ToList();
        return shown.FirstOrDefault(s => !IsBehind(s)) ?? shown.FirstOrDefault(); // si todo quedó atrás, el primero
    }

    /// <summary>
    /// Pasos de la línea que quedaron atrás (<see cref="IsBehind"/>). No traban el renglón: en la 20261029-220928 un
    /// segundo molino que nunca se hizo tapaba Construir toda la partida. Se muestran aparte ("Quedó sin hacer").
    /// </summary>
    public IReadOnlyList<StepProgress> LaneBehind(Lane lane)
    {
        var row = LaneStep(lane);
        return _steps.Where(s => Lanes.Of(s.Step) == lane && Shown(s) && IsBehind(s) && s.Index != row?.Index).ToList();
    }

    private bool Shown(StepProgress s) => IsOpen(s) && !IsProducing(s) && !IsSilent(s) && _eligible[s.Index];

    /// <summary>
    /// Un paso quedó atrás si, desde que se pudo hacer, el log confirmó (o se empezó) uno posterior de su línea que no
    /// es de su mismo paso mixto. No si está a medias (las galerías 1/2) ni si es de aldeanos (se cuentan por el total).
    /// El emplumado que recién se puede con la herrería no queda atrás de la collera hecha antes.
    /// </summary>
    private bool IsBehind(StepProgress s)
    {
        var lane = Lanes.Of(s.Step);
        if (lane == Lane.Villagers || s.Completed > 0 || !_eligibleSince.TryGetValue(s.Index, out var since))
            return false;
        return _steps.Any(o => o.Index > s.Index && Lanes.Of(o.Step) == lane && !SameGroup(o.Index, s.Index)
            && (_allocated.ContainsKey(o.Index) || _started.ContainsKey(o.Index))
            && (o.DoneAt ?? o.StartedAt) is { } at && at >= since);
    }

    /// <summary>Desde cuándo cada paso es elegible (se borra si deja de serlo).</summary>
    private readonly Dictionary<int, double> _eligibleSince = [];

    /// <summary>
    /// Quedan pasos de la BO por hacer, se puedan mostrar o no (subiendo de edad, lo que sigue es de la edad siguiente y
    /// la BO no terminó). No cuentan lo que el juego no avisa ni la producción en marcha, que nunca se cierran solos.
    /// </summary>
    public bool HasOpenSteps => _steps.Any(s => IsOpen(s) && !IsProducing(s) && !IsSilent(s));

    /// <summary>
    /// La edad a la que llegó el jugador: la de los clics de edad hechos (pasos o log) o salteados, o la de lo que ya se
    /// hizo (una galería dice Feudal). Un clic en curso no cuenta.
    /// </summary>
    public Age Age { get; private set; } = Age.Dark;

    /// <summary>
    /// Hay al menos uno de ese edificio: el centro urbano del arranque, uno que avisó el log (o que dedujo
    /// <see cref="SnapshotBuilder"/>), o un paso de la BO que lo construye y quedó hecho.
    /// </summary>
    public bool Built(string building) => _built.Contains(building);

    /// <summary>
    /// El paso se puede mostrar: es de la edad del jugador o de una anterior, existe el edificio que lo hace (o el que
    /// pide para construirse), los pasos de su <c>after</c> están hechos y, si es un clic de edad, están los 2 edificios
    /// que pide. No mira recursos, población ni hora: con eso se muestra esperando.
    /// </summary>
    public bool IsEligible(int index) => _eligible[index];

    private bool[] _eligible = [];
    private HashSet<string> _built = [];

    /// <summary>
    /// Producción militar en marcha ("12 arqueros" con alguno ya hecho): sigue sola en sus edificios mientras la
    /// economía avanza, así que no frena la BO (con 12 + 12 arqueros el coach quedaba 7 minutos sin decir otra cosa).
    /// </summary>
    public IEnumerable<StepProgress> Producing => _steps.Where(s => IsOpen(s) && IsProducing(s));

    /// <summary>
    /// Lo que el juego nunca avisa (las granjas), fuera de un paso mixto, que ya le toca al jugador: no puede ser el
    /// paso actual porque nada lo confirmaría y la BO quedaba trabada (en la 20261018-132830, "Construir 4 granjas"
    /// desde las 18:00 hasta el final). Se muestra aparte y queda hecho cuando avanza un paso posterior.
    /// </summary>
    public IEnumerable<StepProgress> Silent => Current is { } current
        ? _steps.Where(s => s.Index < current.Index && IsOpen(s) && IsSilent(s))
        : _steps.Where(s => IsOpen(s) && IsSilent(s));

    private static bool IsSilent(StepProgress s) =>
        s.Step.Group is null && EventConventions.EventFor(s.Step) is { } id && EventConventions.Unannounced.Contains(id);

    /// <summary>
    /// Unidades que no son aldeanos y ya empezaron a salir: de este paso, o de un paso anterior con la misma unidad
    /// (el segundo "12 arqueros" sigue al primero).
    /// </summary>
    private bool IsProducing(StepProgress s) =>
        s.Step is { Do: StepAction.Train, What: { } what } && what != "villager"
        && (s.Completed > 0 || _steps.Take(s.Index).Any(p => p.Step.Do == StepAction.Train && p.Step.What == what && p.Completed > 0));

    /// <summary>
    /// Si el paso actual es parte de un paso mixto (<see cref="Step.Group"/>): todas sus metas, en el orden de la BO y
    /// en el estado en que estén (hechas, en curso o pendientes). Vacío si es un paso simple.
    /// </summary>
    public IReadOnlyList<StepProgress> CurrentGoals => Current is { Step.Group: { } group }
        ? _steps.Where(s => s.Step.Group == group).ToList()
        : [];

    /// <summary>El primer paso pendiente después del actual que no es otra meta del mismo paso mixto.</summary>
    public StepProgress? Next => Current is { } current
        ? _steps.Where(s => IsOpen(s) && !IsProducing(s)).FirstOrDefault(s => s.Index > current.Index && !SameGroup(s.Index, current.Index))
        : null;

    /// <summary>Son metas del mismo paso mixto: se hacen a la vez, así que su orden no dice nada.</summary>
    private bool SameGroup(int i, int j) => _bo.Steps[i].Group is { } g && g == _bo.Steps[j].Group;

    public IEnumerable<StepProgress> InProgress => _steps.Where(s => s.State == StepState.InProgress);

    public bool Finished => _steps.All(s => s.State is StepState.Done or StepState.Skipped);

    public void Update(PlayerState state)
    {
        _state = state;
        TrackHud();
        Recompute();
    }

    /// <summary>
    /// Un paso de aldeanos queda hecho cuando el HUD muestra en su recurso el total que pide la BO al terminarlo,
    /// durante <see cref="HudConfirmSeconds"/>. Ya hecho no se deshace si después se mueven aldeanos.
    /// </summary>
    private void TrackHud()
    {
        TrackCap();
        if (_state.VillagersOn is not { } on)
            return;
        for (var i = 0; i < _hudTarget.Length; i++)
        {
            if (_hudTarget[i] is not { } target || _hudDone.ContainsKey(i) || _noHud.Contains(i))
                continue;
            if (on[target.Resource] < target.Total)
            {
                _hudSince.Remove(i);
                continue;
            }
            if (!_hudSince.TryGetValue(i, out var since))
                _hudSince[i] = since = _state.Time;
            if (_state.Time - since >= HudConfirmSeconds)
                _hudDone[i] = since;
        }
    }

    /// <summary>Pasos de casas hechos por el tope de población: como los del HUD, tiene que sostenerse 2 s.</summary>
    private void TrackCap()
    {
        for (var i = 0; i < _capTarget.Length; i++)
        {
            if (_capTarget[i] is not { } target || _hudDone.ContainsKey(i) || _noHud.Contains(i))
                continue;
            if (_state.PopulationCap < target)
            {
                _hudSince.Remove(i);
                continue;
            }
            if (!_hudSince.TryGetValue(i, out var since))
                _hudSince[i] = since = _state.Time;
            if (_state.Time - since >= HudConfirmSeconds)
                _hudDone[i] = since;
        }
    }

    /// <summary>
    /// Hotkey "avanzar": da por hecho el paso de esa línea, o sin línea lo primero que falta (<see cref="Current"/>).
    /// </summary>
    public void Advance(Lane? lane = null)
    {
        if ((lane is { } l ? LaneStep(l) : Current) is { } target)
        {
            _manualDone[target.Index] = _state.Time;
            _noImplicit.Remove(target.Index);
            Recompute();
        }
    }

    /// <summary>Hotkey "saltear": marca como salteado el paso de esa línea, o sin línea lo primero que falta.</summary>
    public void Skip(Lane? lane = null)
    {
        if ((lane is { } l ? LaneStep(l) : Current) is { } target)
        {
            _skipped[target.Index] = _state.Time;
            Recompute();
        }
    }

    /// <summary>
    /// El paso ya se pagó y se está haciendo (por ejemplo, se vio la baja de comida del clic en Feudal). Deja de
    /// ser el paso actual y el Planner cuenta lo que le falta desde ese momento.
    /// </summary>
    public void MarkStarted(int index, double time)
    {
        _started[index] = time;
        Recompute();
    }

    /// <summary>Lo pagado se devolvió (se canceló): el paso vuelve a estar pendiente.</summary>
    public void ClearStarted(int index)
    {
        if (_started.Remove(index))
            Recompute();
    }

    /// <summary>
    /// Hotkey "retroceder": deshace lo último que se completó, salteó o empezó. Si lo habían confirmado eventos,
    /// esos eventos se descartan (se asume que fueron una lectura falsa).
    /// </summary>
    public void Back()
    {
        var last = _steps
            .Select(s => (s.Index, Time: s.State switch
            {
                StepState.Skipped => _skipped[s.Index],
                StepState.InProgress => s.StartedAt,
                _ => s.DoneAt,
            }))
            .Where(s => s.Time is not null)
            .OrderBy(s => s.Time)
            .ThenBy(s => s.Index)
            .LastOrDefault();
        if (last.Time is null)
            return;

        var i = last.Index;
        if (_steps[i].State == StepState.InProgress)
        {
            _started.Remove(i);
        }
        else if (_hudDone.Remove(i))
        {
            _noHud.Add(i); // el jugador dice que no: el HUD ya no lo da por hecho
        }
        else if (!_skipped.Remove(i) && !_manualDone.Remove(i))
        {
            if (_allocated.TryGetValue(i, out var events) && events.Count > 0)
                _ignored.UnionWith(events);
            else
                _noImplicit.Add(i);
        }
        Recompute();
    }

    private string? ConfirmableEvent(Step step)
    {
        var id = EventConventions.EventFor(step);
        return id is not null && !EventConventions.Unannounced.Contains(id) && (_events is null || _events.Contains(id)) ? id : null;
    }

    private int Required(int index) => _units[index];

    private static bool IsOpen(StepProgress s) => s.State is StepState.Pending or StepState.Ready;

    private void Recompute()
    {
        var n = _bo.Steps.Count;
        var completed = new int[n];
        var doneAt = new double?[n];
        _allocated.Clear();
        _extras = [];

        // 1. Cada evento confirma el primer paso (en orden de la BO) que lo espera y todavía no se completó.
        foreach (var e in _state.Events)
        {
            if (_ignored.Contains(e))
                continue;
            var target = -1;
            for (var i = 0; i < n; i++)
            {
                // Un paso que el HUD dio por hecho se queda con los aldeanos que salieron antes; los de después siguen.
                if (_confirmEvent[i] == e.EventId && !_skipped.ContainsKey(i) && !_manualDone.ContainsKey(i)
                    && !(_hudDone.TryGetValue(i, out var hudAt) && e.Time > hudAt)
                    && completed[i] < Required(i))
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                if (EventConventions.Parse(e.EventId) is not null)
                    _extras.Add(new ExtraAction(e));
                continue;
            }

            completed[target]++;
            (_allocated.TryGetValue(target, out var list) ? list : _allocated[target] = []).Add(e);
            if (completed[target] == Required(target))
                doneAt[target] = e.Time;
        }

        // 1b. Pasos de aldeanos según el HUD: hechos si mostró el total, y si no, lo que ya hay en el recurso.
        for (var i = 0; i < n; i++)
        {
            if (_hudDone.TryGetValue(i, out var hudAt))
            {
                doneAt[i] ??= hudAt;
                completed[i] = Required(i);
            }
            else if (_hudTarget[i] is { } target && _state.VillagersOn is { } on && !_noHud.Contains(i))
            {
                completed[i] = Math.Max(completed[i], Required(i) - Math.Max(0, target.Total - (int)Math.Round(on[target.Resource])));
            }
        }

        bool IsDone(int i) => doneAt[i] is not null || _manualDone.ContainsKey(i);

        // 2. Pasos que el log no puede confirmar: hechos cuando avanza algún paso posterior.
        for (var i = n - 1; i >= 0; i--)
        {
            if (_confirmEvent[i] is not null || IsDone(i) || _skipped.ContainsKey(i) || _noImplicit.Contains(i))
                continue;
            // Lo que el juego nunca avisa (granjas), en un paso mixto: hecho cuando las otras metas están hechas (los
            // aldeanos a granjas y sus granjas). Lo que el log avisa pero no se pudo leer espera a un paso posterior:
            // terminar el cuartel no dice que la Feudal terminó.
            var mates = EventConventions.EventFor(_bo.Steps[i]) is { } silent && EventConventions.Unannounced.Contains(silent)
                ? Enumerable.Range(0, n).Where(j => j != i && SameGroup(i, j)).ToList()
                : [];
            if (mates.Count > 0)
            {
                // Solo con sus compañeras: en la 20261018-132830 el telar terminado temprano (4:21) daba por hecha la
                // granja de "1 aldeano a granjas" y dejaba atrasados la casa y el molino de antes.
                if (mates.All(IsDone))
                    doneAt[i] = mates.Max(j => doneAt[j] ?? _manualDone.GetValueOrDefault(j));
                continue;
            }
            // Una investigación empezada da por hechas las granjas sueltas de antes (el clic en Castillos con las granjas
            // pendientes), pero no lo de un paso mixto: el telar se puede pagar temprano sin haber hecho la granja. Vale
            // cualquier línea: lo que nada confirma (granjas, un mensaje que el log no lee) quedaría abierto para siempre.
            var loose = _bo.Steps[i].Group is null && EventConventions.EventFor(_bo.Steps[i]) is { } ev && EventConventions.Unannounced.Contains(ev);
            var progressAfter = Enumerable.Range(i + 1, n - i - 1)
                .Where(j => !SameGroup(i, j) && (completed[j] > 0 || IsDone(j) || (loose && _started.ContainsKey(j))))
                .Select(j => FirstProgressTime(j, doneAt))
                .Min();
            if (progressAfter is not null)
                doneAt[i] = progressAfter;
        }

        // 2b. Edad, edificios y qué pasos se pueden mostrar (spec 2026-10-30: nada de otra edad ni sin su edificio).
        var civ = _data.Civ(_bo.Civ);
        _built = [BuildOrderValidator.StartingBuilding];
        var age = Age.Dark;
        // Lo hecho dice la edad aunque el log no haya leído su aviso: una galería es de Feudal, así que se llegó.
        void Reached(StepAction action, string what)
        {
            if (action == StepAction.Build)
            {
                _built.Add(what);
                if (_data.TryGet(what, out var building))
                    _built.UnionWith(building.CountsAs); // el asentamiento de los incas
            }
            if (action == StepAction.Research && BuildOrderValidator.AgeOf(what) is { } clicked && clicked > age)
                age = clicked;
            else if (_data.TryGet(what, out var entity) && entity.Age > age)
                age = entity.Age;
        }
        foreach (var e in _state.Events)
        {
            if (EventConventions.Parse(e.EventId) is { } parsed)
                Reached(parsed.Action, parsed.What);
        }
        for (var i = 0; i < n; i++)
        {
            // Lo salteado a mano cuenta como hecho: un clic de edad salteado pasa a la edad siguiente y un edificio
            // salteado no deja ocultos para siempre a los que lo piden (la BO nunca terminaba).
            if ((IsDone(i) || _skipped.ContainsKey(i)) && _bo.Steps[i].What is { } what)
                Reached(_bo.Steps[i].Do, what);
        }
        Age = age;
        _eligible = Enumerable.Range(0, n).Select(i => Eligible(_bo.Steps[i])).ToArray();
        for (var i = 0; i < n; i++)
        {
            if (!_eligible[i])
                _eligibleSince.Remove(i);
            else
                _eligibleSince.TryAdd(i, _state.Time);
        }

        bool Eligible(Step step)
        {
            // Un paso salteado cuenta como hecho para los que dependen de él.
            if (step.After.Any(dep => _bo.Steps.Select((s, j) => (s, j)).Any(x => x.s.Id == dep && !IsDone(x.j) && !_skipped.ContainsKey(x.j))))
                return false;
            if (step.What is null || !_data.TryGet(step.What, out var entity))
                return true;
            // La edad de game_data ya es la de antes para un clic de edad (feudal_age es de la Edad Oscura).
            if (entity.Age > age)
                return false;
            if (step.Do is StepAction.Train or StepAction.Research && entity.MadeIn is { } producer && !_built.Contains(producer))
                return false;
            if (step.Do == StepAction.Build && civ.Requires(entity) is { } required && !_built.Contains(required))
                return false;
            // Los jemeres suben sin edificios (game_data no_age_buildings).
            return civ.NoAgeBuildings || BuildOrderValidator.AgeUpRequirement(step.What) is null || BuildOrderValidator.AgeUpProgress(step.What, _built) >= 2;
        }

        // 3. Estado final de cada paso.
        var steps = new StepProgress[n];
        for (var i = 0; i < n; i++)
        {
            var step = _bo.Steps[i];
            var required = Required(i);
            StepState state;
            double? at = doneAt[i];
            var manual = false;

            if (_skipped.ContainsKey(i))
            {
                state = StepState.Skipped;
            }
            else if (required == 0)
            {
                state = StepState.Done; // ya había esos aldeanos en la fuente: el paso no pide ninguno
            }
            else if (_manualDone.TryGetValue(i, out var manualAt))
            {
                state = StepState.Done;
                at = manualAt;
                manual = true;
                completed[i] = required;
            }
            else if (doneAt[i] is not null)
            {
                state = StepState.Done;
                completed[i] = required;
            }
            else if (_started.ContainsKey(i))
            {
                state = StepState.InProgress;
            }
            else
            {
                state = IsReady(step, i, completed[i]) ? StepState.Ready : StepState.Pending;
            }

            steps[i] = new StepProgress(step, i, state, Math.Min(completed[i], required), required, state == StepState.Done ? at : null, manual,
                state == StepState.InProgress ? _started[i] : null);
        }
        _steps = steps;

        bool IsReady(Step step, int i, int done)
        {
            if (!_eligible[i])
                return false;
            // La población y la hora no esconden el paso (como los recursos, se espera mostrándolo): "Feudal a 20 de
            // población" se ve antes de llegar a 20.
            if (step.AtPop is { } pop && _state.Population < pop)
                return false;
            if (step.AtTime is { } t && _state.Time < t)
                return false;
            if (step.What is null || !_data.TryGet(step.What, out var entity))
                return step.Do == StepAction.Move;
            if (entity.Pop > 0 && _state.Population >= _state.PopulationCap)
                return false;
            return _state.Stock.Covers(entity.Cost);
        }
    }

    private double? FirstProgressTime(int i, double?[] doneAt) =>
        _manualDone.TryGetValue(i, out var manual) ? manual
        : _allocated.TryGetValue(i, out var events) && events.Count > 0 ? events[0].Time
        : _started.TryGetValue(i, out var started) ? started // una investigación pagada: el jugador ya siguió
        : doneAt[i];
}
