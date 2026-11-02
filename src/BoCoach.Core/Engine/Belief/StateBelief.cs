using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// La creencia del estado (spec <c>docs/superpowers/specs/2026-11-02-creencia-del-estado-design.md</c>): una sola capa
/// que fusiona lo que las fuentes de la pantalla dicen de la partida y entrega lo que el coach cree que pasó. No sabe
/// nada de la BO: recibe lecturas (<see cref="Observe"/>) y devuelve creencias (<see cref="View"/>) y lo que terminó
/// desde la captura anterior (<see cref="TakeNew"/>), como eventos para <see cref="PlayerState"/> y <see cref="Tracker"/>.
/// <para>Cada fuente aporta evidencia con una fuerza (<see cref="Strength"/>). Las cuentas (edificios, unidades) son
/// cotas inferiores y lo creído es el máximo de lo que cada fuente afirma: el log avisó una galería y la cola muestra
/// dos arqueros a la vez, entonces hay dos. Una evidencia no puede afirmar lo que una creencia más fuerte hace
/// imposible: la cola no suma edificios de una edad que el log no dio por alcanzada.</para>
/// <para>Las fuentes que no son el log producen completaciones inferidas que esperan un tiempo el aviso del log: si
/// llega, lo absorben (no es otra cosa). Orden dentro de una captura: el log, el tope (casas), la cola, el puntaje
/// (con lo creído hasta ahí como crédito) y la población menos los aldeanos (unidades).</para>
/// </summary>
public sealed class StateBelief
{
    /// <summary>Segundos que una completación inferida espera el aviso del log antes de darlo por otra cosa.</summary>
    public const double CreditSeconds = 20;

    /// <summary>Lo mismo para lo que el log muestra al instante (un edificio deducido por la cola): el aviso llega como mucho dos capturas después.</summary>
    public const double LogLag = 2;

    private const int MaxPopulation = BuildOrderValidator.MaxPopulation;

    private readonly GameData _data;
    private readonly CivBonuses _civ;
    private readonly QueueSource _queue;
    private readonly CapSource _cap;
    private readonly UnitSource _units;
    private readonly ScoreSource _score;

    private readonly Dictionary<string, int> _buildings = new() { [BuildOrderValidator.StartingBuilding] = 1 };
    private readonly Dictionary<string, ResearchStatus> _research = [];
    private readonly Dictionary<string, int> _unitCounts = [];
    private readonly List<Completion> _new = [];
    private readonly List<(string EventId, double Until)> _pending = [];
    private int _builtCap;
    private Age _age = Age.Dark;
    private int? _villagers;
    private ResourceAmounts? _villagersOn;
    private int? _population;
    private int? _populationCap;

    public StateBelief(GameData data, CivBonuses? civ = null)
    {
        _data = data;
        _civ = civ ?? CivBonuses.None;
        _queue = new QueueSource(data);
        _cap = new CapSource(data, _civ);
        _units = new UnitSource(data);
        _score = new ScoreSource(data);
        _builtCap = PlanStart.For(_civ, data).PopulationCap;
        View = Snapshot();
    }

    /// <summary>El número del jugador en el panel de puntajes (de la pantalla de carga); si no se sabe, se deduce.</summary>
    public int? PlayerSlot { get => _score.PlayerSlot; set => _score.PlayerSlot = value; }

    public BeliefView View { get; private set; }

    /// <summary>Hay completaciones que todavía nadie tomó con <see cref="TakeNew"/>.</summary>
    public bool HasNew => _new.Count > 0;

    /// <summary>Si la pantalla puede mostrar que eso terminó: el juego no avisa las granjas (<see cref="EventConventions.Unannounced"/>).</summary>
    public static bool CanObserve(string eventId) => !EventConventions.Unannounced.Contains(eventId);

    /// <summary>Una captura. <paramref name="now"/> es el reloj crudo (el último si esta captura no lo trae).</summary>
    public void Observe(ScreenReading reading, double now)
    {
        _pending.RemoveAll(p => now > p.Until);
        var before = _new.Count;
        foreach (var id in reading.NewEvents)
            Announced(id, now);
        if (reading.PopulationCap is { } cap)
        {
            for (var houses = _cap.HousesFrom(cap, now); houses > 0; houses--)
                Add("house_built", EvidenceSource.PopulationCap, Strength.Observed, now, CreditSeconds);
        }
        if (reading.Queue is { } queue)
            FromQueue(queue, now);
        _score.Logged(_new.Skip(before).Select(c => c.EventId).ToList(), now);
        if (reading.Scores is { } scores)
        {
            foreach (var building in _score.Update(scores, now))
                Add(EventConventions.EventFor(StepAction.Build, building)!, EvidenceSource.Score, Strength.Inferred, now, LogLag);
        }
        Hud(reading, now);
        View = Snapshot();
    }

    /// <summary>Lo creído desde la última llamada, en orden (también lo que el log avisó).</summary>
    public IReadOnlyList<Completion> TakeNew()
    {
        var taken = _new.ToList();
        _new.Clear();
        return taken;
    }

    private void Announced(string id, double now)
    {
        if (Absorb(id))
            return;
        Add(id, EvidenceSource.Log, Strength.Announced, now);
    }

    /// <summary>Una completación inferida que esperaba este aviso: el aviso no es otra cosa.</summary>
    private bool Absorb(string id)
    {
        var index = _pending.FindIndex(p => p.EventId == id);
        if (index < 0)
            return false;
        _pending.RemoveAt(index);
        return true;
    }

    /// <summary>Cree que terminó lo que dice el evento y lo deja para emitir. <paramref name="waitLog"/>: segundos que espera el aviso del log.</summary>
    private void Add(string id, EvidenceSource source, Strength strength, double now, double? waitLog = null)
    {
        _new.Add(new Completion(id, source, strength));
        if (waitLog is { } wait)
            _pending.Add((id, now + wait));
        if (EventConventions.Parse(id) is not { } parsed)
            return; // un aviso que no es una acción ("Farm Exhausted", ataque): pasa, sin cambiar lo creído
        switch (parsed.Action)
        {
            case StepAction.Build:
                _buildings[parsed.What] = _buildings.GetValueOrDefault(parsed.What) + 1;
                if (_data.TryGet(parsed.What, out var building))
                {
                    if (_civ.PopBonus(building, Age.Dark) is > 0 and var pop)
                    {
                        _builtCap += pop;
                        _cap.Credit(pop, now);
                    }
                    // El Settlement de los incas vale por campamento maderero, minero, molino y casa: cada uno avisa como
                    // construido. La población ya la dio el propio edificio.
                    foreach (var counted in building.CountsAs)
                    {
                        _buildings[counted] = _buildings.GetValueOrDefault(counted) + 1;
                        _new.Add(new Completion(EventConventions.EventFor(StepAction.Build, counted)!, source, strength));
                    }
                }
                break;
            case StepAction.Train when id != EventConventions.VillagerCreated:
                _unitCounts[parsed.What] = _unitCounts.GetValueOrDefault(parsed.What) + 1;
                if (source == EvidenceSource.Log)
                {
                    foreach (var extra in _units.Logged(id, now))
                        Add(extra, EvidenceSource.Population, Strength.Inferred, now);
                }
                if (_units.ProducersOf(id, now) is { } producers)
                {
                    for (var known = _buildings.GetValueOrDefault(producers.Building); known < producers.Count; known++)
                        Add(EventConventions.EventFor(StepAction.Build, producers.Building)!, EvidenceSource.Production, Strength.Observed, now, LogLag);
                }
                break;
            case StepAction.Research:
                _research[parsed.What] = ResearchStatus.Done;
                break;
        }
        Reached(parsed.Action, parsed.What);
    }

    /// <summary>Lo hecho dice la edad aunque el log no haya leído su aviso: una galería es de Feudal, así que se llegó.</summary>
    private void Reached(StepAction action, string what)
    {
        if (action == StepAction.Research && BuildOrderValidator.AgeOf(what) is { } clicked)
        {
            if (clicked > _age)
                _age = clicked;
        }
        else if (_data.TryGet(what, out var entity) && entity.Age > _age)
        {
            _age = entity.Age;
        }
    }

    private void FromQueue(IReadOnlyList<QueueItem> queue, double now)
    {
        var facts = _queue.Update(queue, now, _age, entity => _civ.Time(entity, _age));
        foreach (var (building, count) in facts.Producers)
        {
            for (var known = _buildings.GetValueOrDefault(building); known < count; known++)
                Add(EventConventions.EventFor(StepAction.Build, building)!, EvidenceSource.Queue, Strength.Observed, now, LogLag);
        }
        foreach (var tech in facts.InProgress)
            _research.TryAdd(tech, ResearchStatus.InProgress);
        foreach (var tech in facts.Finished)
        {
            if (_research.GetValueOrDefault(tech) != ResearchStatus.Done)
                Add(EventConventions.EventFor(StepAction.Research, tech)!, EvidenceSource.Queue, Strength.Observed, now, CreditSeconds);
        }
    }

    private void Hud(ScreenReading reading, double now)
    {
        // Más aldeanos que el tope de población del juego es una lectura mala: se descarta.
        var readVillagers = reading.Villagers is { } read && read <= MaxPopulation ? read : (int?)null;
        _villagers = readVillagers ?? _villagers;
        _villagersOn = reading.VillagersOn ?? _villagersOn;
        if (reading.Population is not { } population || reading.PopulationCap is not { } cap)
            return;
        (_population, _populationCap) = (population, cap);
        if (readVillagers is { } villagers && population >= villagers)
        {
            foreach (var unit in _units.FromPopulation(population - villagers, now))
                Add(unit, EvidenceSource.Population, Strength.Inferred, now, CreditSeconds);
        }
    }

    private BeliefView Snapshot() => new(
        new Dictionary<string, int>(_buildings),
        new Dictionary<string, ResearchStatus>(_research),
        new Dictionary<string, int>(_unitCounts),
        _villagers, _villagersOn, _population, _populationCap, _age, _builtCap);
}
