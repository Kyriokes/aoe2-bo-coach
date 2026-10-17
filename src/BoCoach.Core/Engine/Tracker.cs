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

    /// <summary>
    /// Se terminó (o se empezó) un paso posterior que hace el mismo actor, y este sigue sin empezar. No se pierde:
    /// se muestra aparte.
    /// </summary>
    Late,

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
/// <para>Cada paso lo hace un "actor": el edificio que entrena o investiga (centro urbano, cuartel...) o los
/// aldeanos que construyen. Los actores trabajan en paralelo, así que un paso queda atrasado solo si el mismo
/// actor ya terminó uno posterior: construir el campamento antes de tiempo no atrasa los aldeanos del centro urbano.</para>
/// </summary>
public sealed class Tracker
{
    private readonly BuildOrder _bo;
    private readonly GameData _data;
    private readonly EventCatalog? _events;
    private readonly string?[] _confirmEvent;
    private readonly string[] _actor;
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

    /// <param name="events">Eventos que Vision sabe reconocer. Un paso cuyo evento no está acá no se puede
    /// confirmar por el log y se da por hecho cuando avanza un paso posterior. Null = todos reconocibles.</param>
    public Tracker(BuildOrder bo, GameData data, EventCatalog? events = null)
    {
        _bo = bo;
        _data = data;
        _events = events;
        _confirmEvent = bo.Steps.Select(ConfirmableEvent).ToArray();
        _units = LivePlan.Units(bo);
        _actor = bo.Steps.Select(Actor).ToArray();
        Recompute();
    }

    public BuildOrder BuildOrder => _bo;
    public IReadOnlyList<StepProgress> Steps => _steps;
    public IReadOnlyList<ExtraAction> Extras => _extras;
    public PlayerState State => _state;

    /// <summary>Paso a mostrar en grande: el primero pendiente que no quedó atrasado.</summary>
    public StepProgress? Current => _steps.FirstOrDefault(IsOpen);

    /// <summary>
    /// Si el paso actual es parte de un paso mixto (<see cref="Step.Group"/>): todas sus metas, en el orden de la BO y
    /// en el estado en que estén (hechas, en curso o pendientes). Vacío si es un paso simple.
    /// </summary>
    public IReadOnlyList<StepProgress> CurrentGoals => Current is { Step.Group: { } group }
        ? _steps.Where(s => s.Step.Group == group).ToList()
        : [];

    /// <summary>El primer paso pendiente después del actual que no es otra meta del mismo paso mixto.</summary>
    public StepProgress? Next => Current is { } current
        ? _steps.Where(IsOpen).FirstOrDefault(s => s.Index != current.Index && !SameGroup(s.Index, current.Index))
        : null;

    /// <summary>Son metas del mismo paso mixto: se hacen a la vez, así que su orden no dice nada.</summary>
    private bool SameGroup(int i, int j) => _bo.Steps[i].Group is { } g && g == _bo.Steps[j].Group;

    public IEnumerable<StepProgress> Late => _steps.Where(s => s.State == StepState.Late);

    public IEnumerable<StepProgress> InProgress => _steps.Where(s => s.State == StepState.InProgress);

    public bool Finished => _steps.All(s => s.State is StepState.Done or StepState.Skipped);

    public void Update(PlayerState state)
    {
        _state = state;
        Recompute();
    }

    /// <summary>Hotkey "avanzar": da por hecho el paso actual.</summary>
    public void Advance()
    {
        if (Current is { } current)
        {
            _manualDone[current.Index] = _state.Time;
            _noImplicit.Remove(current.Index);
            Recompute();
        }
    }

    /// <summary>Hotkey "saltear": marca el paso actual como salteado.</summary>
    public void Skip()
    {
        if (Current is { } current)
        {
            _skipped[current.Index] = _state.Time;
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
        return id is not null && (_events is null || _events.Contains(id)) ? id : null;
    }

    private int Required(int index) => _units[index];

    /// <summary>Quién hace el paso: el edificio que lo produce, o los aldeanos si es construir o mover.</summary>
    private string Actor(Step step) =>
        step.Do is StepAction.Train or StepAction.Research && step.What is not null && _data.TryGet(step.What, out var entity)
            ? entity.MadeIn ?? ""
            : "aldeanos";

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
                if (_confirmEvent[i] == e.EventId && !_skipped.ContainsKey(i) && !_manualDone.ContainsKey(i)
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

        bool IsDone(int i) => doneAt[i] is not null || _manualDone.ContainsKey(i);

        // 2. Pasos que el log no puede confirmar: hechos cuando avanza algún paso posterior.
        for (var i = n - 1; i >= 0; i--)
        {
            if (_confirmEvent[i] is not null || IsDone(i) || _skipped.ContainsKey(i) || _noImplicit.Contains(i))
                continue;
            var progressAfter = Enumerable.Range(i + 1, n - i - 1)
                .Where(j => !SameGroup(i, j) && (completed[j] > 0 || IsDone(j)))
                .Select(j => FirstProgressTime(j, doneAt))
                .Min();
            if (progressAfter is not null)
                doneAt[i] = progressAfter;
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
            else if (completed[i] == 0 && Enumerable.Range(i + 1, n - i - 1).Any(j => _actor[j] == _actor[i] && !SameGroup(i, j) && (IsDone(j) || _started.ContainsKey(j))))
            {
                state = StepState.Late;
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
            if (step.After.Any(dep => _bo.Steps.Select((s, j) => (s, j)).Any(x => x.s.Id == dep && !IsDone(x.j))))
                return false;
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
        : doneAt[i];
}
