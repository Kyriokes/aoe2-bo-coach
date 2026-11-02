using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Las unidades que no son aldeanos, por el log y por la población (fuerza Inferida), y los edificios que delata su
/// ritmo (fuerza Observada):
/// <list type="bullet">
/// <item>Con el "N/M" y el total de aldeanos, las unidades militares son la diferencia. Si suben más de lo que avisó
/// el log (dos galerías sacan un arquero cada una y el log muestra un solo renglón), las de más son del tipo del último
/// aviso de unidad; si todavía no hubo ninguno, quedan sin tipo hasta que el log diga cuál fue.</item>
/// <item>Si salen más unidades iguales juntas de las que pueden sacar los edificios conocidos, hay más edificios: el
/// log no agrega renglón si dos terminan a la vez (partida 20261018-011721: dos galerías, un solo "Archery Range
/// Built", arqueros a las 12:40 y 12:45).</item>
/// </list>
/// </summary>
internal sealed class UnitSource(GameData data)
{
    /// <summary>Si la población no se leyó en este tiempo, el log pudo avisar esas unidades: se arranca de nuevo.</summary>
    private const double GapSeconds = CapSource.CapGapSeconds;

    /// <summary>
    /// Segundos que las unidades militares tienen que quedarse en un valor nuevo para tomarlo como firme. El total de
    /// aldeanos y el "N/M" (que cerca del tope el juego muestra una captura sí y otra no) cambian en capturas
    /// distintas: al salir un aldeano, la diferencia baja o sube por unos segundos sin que haya pasado nada (en la
    /// 20261018-001030, 5 s). Una baja (unidades que mueren) se espera más, porque contarla de más haría contar dos
    /// veces la unidad siguiente.
    /// </summary>
    private const double RiseSeconds = 3;
    private const double DropSeconds = 10;

    /// <summary>Unidades de más en una sola suba para creerla (si no, es una lectura mala).</summary>
    private const int MaxUnitsAtOnce = 6;

    /// <summary>
    /// Parte del tiempo de entrenamiento dentro de la que dos unidades iguales tienen que salir de edificios distintos
    /// (ni con las bonificaciones de civ un edificio saca una unidad en la mitad de su tiempo).
    /// </summary>
    private const double SameBuildingWindow = 0.5;

    private int? _steadyMilitary;
    private (int Military, double Since)? _militaryRun;
    private double _lastRead = double.NegativeInfinity;
    private readonly List<double> _logCredits = [];
    private readonly List<double> _unexplained = [];
    private string? _lastUnit;
    private readonly Dictionary<string, List<double>> _times = [];

    /// <summary>
    /// Un "X Created" del log que no es un aldeano. Devuelve las unidades de más que este aviso explica (subas de la
    /// población que esperaban saber de qué tipo eran), aparte de la avisada.
    /// </summary>
    public IReadOnlyList<string> Logged(string id, double now)
    {
        Prune(now);
        _lastUnit = id;
        if (_unexplained.Count == 0)
        {
            _logCredits.Add(now);
            return [];
        }
        // La población subió antes de saber qué era: este aviso dice el tipo de todas (salieron juntas).
        var extra = Enumerable.Repeat(id, _unexplained.Count - 1).ToList();
        _unexplained.Clear();
        return extra;
    }

    /// <summary>Procesa la diferencia entre la población y los aldeanos y devuelve las unidades que el log no avisó.</summary>
    public IReadOnlyList<string> FromPopulation(int military, double now)
    {
        Prune(now);
        if (now - _lastRead > GapSeconds)
            (_steadyMilitary, _militaryRun) = (military, null);
        _lastRead = now;
        if (_steadyMilitary is not { } steady || military == steady)
        {
            _militaryRun = null;
            return [];
        }
        if (_militaryRun is not { } run || run.Military != military)
            _militaryRun = run = (military, now);
        if (now - run.Since < (military > steady ? RiseSeconds : DropSeconds))
            return [];

        var rise = military - steady;
        (_steadyMilitary, _militaryRun) = (military, null);
        if (rise <= 0 || rise > MaxUnitsAtOnce)
            return [];
        // Primero, lo que explican los avisos del log.
        var explained = Math.Min(rise, _logCredits.Count);
        _logCredits.RemoveRange(0, explained);
        var inferred = new List<string>();
        for (var i = explained; i < rise; i++)
        {
            if (_lastUnit is { } id)
                inferred.Add(id);
            else
                _unexplained.Add(now);
        }
        return inferred;
    }

    /// <summary>Cuántos edificios iguales hacen falta para las unidades iguales que salieron en la mitad de su tiempo (0 si no se sabe dónde se hace).</summary>
    public (string Building, int Count)? ProducersOf(string unitEvent, double now)
    {
        if (EventConventions.Parse(unitEvent) is not { Action: BuildOrders.StepAction.Train } unit
            || !data.TryGet(unit.What, out var entity) || entity.MadeIn is not { } building || entity.Time <= 0)
            return null;
        if (!_times.TryGetValue(unit.What, out var times))
            _times[unit.What] = times = [];
        times.RemoveAll(t => now - t > entity.Time * SameBuildingWindow);
        times.Add(now);
        return (building, times.Count);
    }

    private void Prune(double now)
    {
        _logCredits.RemoveAll(t => now - t > StateBelief.CreditSeconds);
        _unexplained.RemoveAll(t => now - t > StateBelief.CreditSeconds);
    }
}
