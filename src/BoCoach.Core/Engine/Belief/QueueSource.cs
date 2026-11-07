using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Lo que la cola de producción dice en una lectura (ver <see cref="QueueSource"/>).</summary>
/// <param name="Producers">Edificio → cuántos hay al menos, según los íconos iguales que avanzan a la vez.</param>
/// <param name="InProgress">Tecnologías en la cola en esta lectura y en la anterior.</param>
/// <param name="Finished">Tecnologías que estuvieron en la cola y ya no están, pasado su tiempo.</param>
/// <param name="Left">Tecnologías que estaban en curso y faltan de la cola hace <see cref="QueueSource.LeftSeconds"/>, sin terminar.</param>
internal sealed record QueueFacts(IReadOnlyDictionary<string, int> Producers, IReadOnlySet<string> InProgress, IReadOnlyList<string> Finished, IReadOnlyList<string> Left)
{
    public static readonly QueueFacts None = new(new Dictionary<string, int>(), new HashSet<string>(), [], []);
}

/// <summary>
/// La cola de producción como fuente de la creencia (fuerza Observada):
/// <list type="bullet">
/// <item>Tiene un ícono por edificio trabajando más los que esperan (teñidos): N íconos iguales que avanzan a la vez
/// son N edificios. En la 20261018-132830 se ven dos arqueros a las 12:14, cuando el log había avisado una sola galería
/// (las dos terminaron juntas) y el coach seguía pidiendo la segunda hasta que salieron los arqueros, a las 12:53. Por
/// las dudas se toma el máximo por clave (no se suman claves distintas) y la cuenta tiene que repetirse en dos lecturas
/// seguidas. Un ícono de algo de una edad que no se alcanzó no cuenta (partida 20261101-230201: una milicia leída
/// como arquero daba una galería a las 9:37, en la Edad Oscura).</item>
/// <item>Una tecnología en la cola en dos lecturas seguidas está en curso. Una que estuvo y falta en dos lecturas
/// seguidas, pasado el 90 % de su tiempo desde que se la vio, terminó aunque el log no lo avise (pedido del usuario del
/// 2026-10-31: el log no es confiable para las tecnologías). Antes de su tiempo, que falte es un error de lectura. Si
/// se la vio menos de la mitad de su tiempo no terminó nada: es un parpadeo (en la 20261101-165020, "town_watch" 3
/// capturas, con el teñido a mitad de camino entre dos íconos) o se canceló. Su tiempo cuenta desde que avanza, no desde
/// que espera (20261103-193834, con todo el árbol encolado: la forja salía terminada a las 25:40 y el log la avisó a las 26:53).</item>
/// <item>Una en curso que falta <see cref="LeftSeconds"/> de la cola se fue sin terminar: se canceló o nunca estuvo (un
/// ícono mal leído dos veces). Si no, quedaba en curso toda la partida. Lo que dura menos es un error de lectura.</item>
/// </list>
/// </summary>
internal sealed class QueueSource(GameData data, CivBonuses civ)
{
    /// <summary>Parte del tiempo de investigación que tiene que pasar para dar por terminada una tecnología que se fue de la cola.</summary>
    public const double FinishedShare = 0.9;

    /// <summary>Parte de su tiempo que tiene que haberse visto una tecnología en la cola para creer que terminó.</summary>
    public const double SeenShare = 0.5;

    /// <summary>Segundos que una tecnología en curso tiene que faltar de la cola para creer que se fue sin terminar.</summary>
    public const double LeftSeconds = 15;

    private Dictionary<string, int> _previousCounts = [];
    private HashSet<string> _previousKeys = [];
    private readonly Dictionary<string, (double First, double Last)> _seen = [];

    /// <summary>Las tecnologías en curso y la última vez que se las vio en la cola.</summary>
    private readonly Dictionary<string, double> _running = [];

    /// <param name="age">La edad alcanzada: lo de una edad mayor es una lectura mala.</param>
    /// <param name="researchTime">Tiempo de investigación de una tecnología (con la civ).</param>
    public QueueFacts Update(IReadOnlyList<QueueItem> queue, double now, Age age, Func<Entity, double> researchTime)
    {
        var counts = new Dictionary<string, int>();
        foreach (var group in queue.Where(i => !i.Waiting).GroupBy(i => i.Key))
        {
            // Una mejora sale del mismo edificio que su unidad: el arbalestero, que la biblioteca ya reconoce, cuenta como arquero.
            if (!(data.TryGet(group.Key, out var entity) || data.TryGet(UnitLines.Base(group.Key), out entity)) || entity.MadeIn is not { } building || !data.TryGet(building, out var producer))
                continue;
            if (civ.AgeOf(entity) > age || civ.AgeOf(producer) > age)
                continue;
            counts[building] = Math.Max(counts.GetValueOrDefault(building), group.Count());
        }
        var producers = new Dictionary<string, int>();
        foreach (var (building, count) in counts)
        {
            if (_previousCounts.GetValueOrDefault(building) >= count)
                producers[building] = count;
        }

        var keys = queue.Select(i => i.Key).ToHashSet();
        var advancing = queue.Where(i => !i.Waiting).Select(i => i.Key).ToHashSet();
        var inProgress = new HashSet<string>();
        var finished = new List<string>();
        foreach (var key in keys)
        {
            if (data.TryGet(key, out var entity) && entity.Kind == EntityKind.Tech && _previousKeys.Contains(key))
            {
                inProgress.Add(key);
                if (advancing.Contains(key))
                    _seen[key] = (_seen.TryGetValue(key, out var seen) ? seen.First : now, now);
            }
        }
        foreach (var (tech, seen) in _seen.ToList())
        {
            if (keys.Contains(tech) || _previousKeys.Contains(tech))
                continue;
            var time = researchTime(data.Get(tech));
            if (now - seen.First >= FinishedShare * time)
            {
                if (seen.Last - seen.First >= SeenShare * time)
                    finished.Add(tech);
                _seen.Remove(tech);
            }
        }

        foreach (var tech in inProgress)
            _running[tech] = now;
        var left = new List<string>();
        foreach (var (tech, lastSeen) in _running.ToList())
        {
            if (keys.Contains(tech))
            {
                _running[tech] = now;
            }
            else if (finished.Contains(tech))
            {
                _running.Remove(tech);
            }
            else if (!_previousKeys.Contains(tech) && now - lastSeen >= LeftSeconds)
            {
                left.Add(tech);
                _running.Remove(tech);
            }
        }

        (_previousCounts, _previousKeys) = (counts, keys);
        return new QueueFacts(producers, inProgress, finished, left);
    }
}
