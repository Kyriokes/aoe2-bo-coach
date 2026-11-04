using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// El panel de puntajes como fuente de la creencia (fuerza Inferida): deduce los edificios que terminan sin aviso del log. El puntaje del jugador sube el 20 %
/// del costo de cada cosa que se termina (una unidad, un edificio, una tecnología) y, lento y parejo, con lo que
/// recolecta. Si "Archery Range Built" ya es el último renglón del log, una segunda galería no cambia nada en el log,
/// pero el puntaje sube otros 35 (partida 20261101-125353: 1415 → 1455 con el aviso y 1464 → 1505 seis segundos
/// después, sin aviso; el coach pidió la tercera galería hasta que la cola mostró tres arqueros a caballo, 3 minutos
/// más tarde).
/// <list type="bullet">
/// <item>El puntaje también lleva una parte del stock: baja al pagar algo (en la 20261101-125353, 1436 → 1411 al pagar
/// dos galerías) y sube con lo que se junta. Esa economía se descuenta con la mediana de las últimas diferencias de ese
/// renglón (una lectura cada 2 s); una baja no se mira.</item>
/// <item>Cada suba se evalúa con la lectura siguiente, porque el aviso del log puede llegar una captura después que el
/// puntaje; lo que avisó el log (o se dedujo por otra señal) explica la suba antes que nada.</item>
/// <item>Lo que sobra se atribuye al último aviso del log solo si es un edificio, calza con su valor (hasta 3 de una
/// vez) y el aviso tiene menos de <see cref="RepeatWindow"/> segundos: después de eso el juego ya habría mostrado el
/// mensaje de nuevo. Las casas y lo que da población no van por acá: las delata el tope.</item>
/// <item>El renglón del jugador es el de su color (<see cref="PlayerSlot"/>, de la pantalla de carga). Si no se sabe,
/// se vota: el renglón cuya suba, descontada su economía, coincide con los avisos del log varias veces seguidas.</item>
/// </list>
/// </summary>
internal sealed class ScoreSource(GameData data)
{
    /// <summary>Parte del costo que el juego suma al puntaje.</summary>
    public const double ScoreShare = 0.2;

    /// <summary>Segundos desde el aviso del log dentro de los que una repetición no se ve en el log.</summary>
    public const double RepeatWindow = 20;

    /// <summary>Tolerancia del valor de un edificio (parte) más unos puntos fijos.</summary>
    private const double Tolerance = 0.25;
    private const double Slack = 4;

    /// <summary>
    /// Lo mismo para deducir un edificio repetido: una galería sube 35 más la economía. Con 0,25 alcanzaba con 23 de
    /// sobra: en la 20261104-095625 varios leñadores descargaron juntos a los 20 s de la galería (+27) y el coach creía
    /// tener 3 con 2.
    /// </summary>
    private const double RepeatTolerance = 0.15;

    /// <summary>Suba mínima (ya descontada la economía) que vale la pena mirar.</summary>
    private const double MinJump = 10;

    /// <summary>Baja que solo explica el clic de una edad (pagar algo baja como mucho unos 40: un centro urbano).</summary>
    private const double BigDrop = 45;

    /// <summary>
    /// Valor mínimo del edificio para deducirlo: con uno de 100 (campamentos, molino: 20 puntos) el ruido de lo que
    /// se paga y se junta en 3 s calza seguido (en la 20261101-125353 salían dos campamentos mineros de más).
    /// </summary>
    private const double MinValue = 30;

    /// <summary>Subas seguidas de un renglón para estimar su economía, las mínimas para usarla y el hueco máximo entre lecturas.</summary>
    private const int DriftSamples = 8;
    private const int DriftMinimum = 3;
    private const double MaxGap = 10;

    /// <summary>Votos que necesita un renglón para tomarlo por el propio, y ventaja sobre el que sigue.</summary>
    private const int VotesNeeded = 4;

    /// <summary>Valor mínimo de los avisos de una lectura para votar con ella: con un aldeano (10) el ruido de lo que se paga confunde.</summary>
    private const double VoteMinimum = 20;

    private readonly Dictionary<int, (double Time, int Score)> _lastBySlot = [];
    private readonly Dictionary<int, List<double>> _history = [];
    private readonly Dictionary<int, int> _votes = [];
    private readonly List<(double Time, double Value)> _credits = [];
    private (double From, double To, double Jump)? _pending;
    private readonly List<(string Building, double Time)> _loggedBuildings = [];

    /// <summary>
    /// Segundos de partida que el aviso del log puede llegar después que la suba del puntaje (dos capturas para
    /// estabilizarse). Lo que avisa el log después de eso explica la suba siguiente, no esta: en la 20261101-020049 la
    /// herrería (17:28) explicaba la segunda galería (17:23, sin aviso) y después la suba de la herrería quedaba sin
    /// explicar y se deducía otra herrería.
    /// </summary>
    private const double LogLag = 2;
    private double _lastReset = double.NegativeInfinity;

    /// <summary>El número del jugador (1 azul ... 8 naranja); null hasta que se sepa o se deduzca.</summary>
    public int? PlayerSlot { get; set; }

    /// <summary>Puntos que suma al terminarse lo que avisa un evento del log (0 si no se sabe).</summary>
    public double Value(string eventId)
    {
        if (EventConventions.Parse(eventId) is not { } parsed || !data.TryGet(parsed.What, out var entity))
            return 0;
        return entity.Cost.Total * ScoreShare;
    }

    /// <summary>Anota los eventos de esta captura (los del log y los deducidos por otras señales).</summary>
    public void Logged(IEnumerable<string> eventIds, double now)
    {
        foreach (var id in eventIds)
        {
            if (EventConventions.Parse(id) is { } parsed && parsed.Action == StepAction.Research && BuildOrderValidator.AgeOf(parsed.What) is not null)
            {
                // Al llegar a una edad el puntaje da un salto propio: se arranca de nuevo.
                Reset(now);
                continue;
            }
            var value = Value(id);
            if (value <= 0)
                continue;
            _credits.Add((now, value));
            if (EventConventions.Parse(id) is { Action: StepAction.Build } built && !GivesPopulation(built.What))
                _loggedBuildings.Add((built.What, now));
        }
    }

    private bool GivesPopulation(string building) => data.TryGet(building, out var entity) && entity.PopBonus > 0;

    /// <summary>Procesa una lectura del panel y devuelve los edificios terminados que el log no avisó.</summary>
    public IReadOnlyList<string> Update(IReadOnlyList<PlayerScore> scores, double now)
    {
        _credits.RemoveAll(c => now - c.Time > RepeatWindow + MaxGap);
        _loggedBuildings.RemoveAll(b => now - b.Time > RepeatWindow + MaxGap);
        var deltas = new Dictionary<int, (double Since, double Delta)>();
        foreach (var row in scores)
        {
            if (_lastBySlot.TryGetValue(row.Slot, out var previous) && now - previous.Time <= MaxGap && now > previous.Time)
                deltas[row.Slot] = (previous.Time, row.Score - previous.Score);
            else
                _history.Remove(row.Slot);
        }
        if (PlayerSlot is null)
            Vote(deltas, now);
        var found = PlayerSlot is { } slot && deltas.TryGetValue(slot, out var own) ? Own(own.Since, own.Delta, now) : [];
        foreach (var (slot2, (_, delta)) in deltas)
        {
            if (!_history.TryGetValue(slot2, out var history))
                _history[slot2] = history = [];
            history.Add(delta);
            if (history.Count > DriftSamples)
                history.RemoveAt(0);
        }
        foreach (var row in scores)
            _lastBySlot[row.Slot] = (now, row.Score);
        return found;
    }

    private double? Drift(int slot) => _history.TryGetValue(slot, out var history) && history.Count >= DriftMinimum ? Median(history) : null;

    /// <summary>La suba del renglón propio: resuelve la pendiente y deja esta para la lectura siguiente.</summary>
    private IReadOnlyList<string> Own(double since, double delta, double now)
    {
        // El puntaje también baja: lleva una parte de lo que hay en stock, así que pagar algo lo descuenta. Una baja
        // chica no es una suba que explicar, nada más; una grande es el clic de una edad (el 10 % de su costo: 50 la
        // Feudal, 180 la Imperial) y en los segundos siguientes el puntaje rebota sin aviso (en las dos partidas de
        // hunos, +38 unos 10 s después del clic de Imperial, que calzaba con el monasterio recién hecho).
        if (delta <= -BigDrop)
        {
            Reset(now);
            return [];
        }
        var found = _pending is { } pending ? Resolve(pending, now) : [];
        _pending = null;
        if (PlayerSlot is { } slot && Drift(slot) is { } drift && now - _lastReset > MaxGap && delta - drift >= MinJump)
            _pending = (since, now, delta - drift);
        return found;
    }

    private void Reset(double now)
    {
        _lastReset = now;
        _pending = null;
        _credits.Clear();
        _loggedBuildings.Clear();
    }

    /// <summary>Lo que sobra de una suba después de descontar lo que avisó el log, atribuido al último edificio avisado.</summary>
    private IReadOnlyList<string> Resolve((double From, double To, double Jump) pending, double now)
    {
        var residual = pending.Jump;
        var kept = new List<(double Time, double Value)>();
        foreach (var credit in _credits.OrderBy(c => c.Time))
        {
            if (credit.Time > pending.From - 1 && credit.Time <= pending.To + LogLag && residual >= credit.Value * 0.5)
                residual -= credit.Value;
            else
                kept.Add(credit);
        }
        _credits.Clear();
        _credits.AddRange(kept);
        // El último edificio avisado antes de la suba (lo avisado después explica la suba que sigue).
        if (_loggedBuildings.LastOrDefault(b => b.Time <= pending.To + LogLag) is not { Building: not null } logged || pending.To - logged.Time > RepeatWindow)
            return [];
        var value = Value(EventConventions.EventFor(StepAction.Build, logged.Building)!);
        if (value < MinValue)
            return [];
        var count = (int)Math.Round(residual / value);
        if (count < 1 || count > 3 || Math.Abs(residual - count * value) > RepeatTolerance * value * count + Slack)
            return [];
        return Enumerable.Repeat(logged.Building, count).ToList();
    }

    /// <summary>
    /// Sin saber el número del jugador: en cada lectura con avisos del log, el renglón que sube lo que valen (descontada
    /// su economía) gana un voto y el que no, lo pierde.
    /// </summary>
    private void Vote(Dictionary<int, (double Since, double Delta)> deltas, double now)
    {
        foreach (var (slot, (since, delta)) in deltas)
        {
            if (Drift(slot) is not { } drift)
                continue;
            var expected = _credits.Where(c => c.Time > since && c.Time <= now).Sum(c => c.Value);
            if (expected < VoteMinimum)
                continue;
            var fits = Math.Abs(delta - drift - expected) <= Tolerance * expected + Slack;
            _votes[slot] = Math.Max(0, _votes.GetValueOrDefault(slot) + (fits ? 1 : -1));
        }
        if (_votes.Count == 0)
            return;
        var best = _votes.MaxBy(v => v.Value);
        var runnerUp = _votes.Where(v => v.Key != best.Key).Select(v => v.Value).DefaultIfEmpty(0).Max();
        if (best.Value >= VotesNeeded && best.Value >= 2 * runnerUp)
        {
            PlayerSlot = best.Key;
            _credits.Clear();
        }
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
