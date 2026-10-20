using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Una señal de la partida (D3): algo que pasó y que puede disparar una rama de la BO.</summary>
/// <param name="Kind">"rival_feudal_click", "rival_castle_click", "rival_imperial_click", "under_attack" o "villagers_lost".</param>
/// <param name="Time">Segundo de partida en que se vio.</param>
/// <param name="Text">El aviso para el overlay.</param>
public sealed record GameSignal(string Kind, double Time, string Text);

/// <summary>
/// Las señales de D3 (diseño acordado con el usuario el 2026-10-20), a partir de los snapshots:
/// <list type="bullet">
/// <item>Clic de edad del rival: en el panel de puntajes, el puntaje cae de golpe la décima parte de lo que cuesta la
/// edad (verificado en la grabación 20261019-202635: el clic de Feudal del usuario bajó 49 puntos, el del rival 34; lo
/// demás en la Edad Oscura, hasta unos 20) y la lectura siguiente sigue abajo. La edad del rival sale del ícono
/// (naranja = Edad Oscura) y de los clics ya vistos. El ícono cambia al llegar, no en el clic. Lo que cae del propio
/// jugador no cuenta: su renglón se sabe por la pantalla de carga o porque cae junto con su propio clic.</item>
/// <item>Te atacan: el aviso del log ("under_attack").</item>
/// <item>Perdiste aldeanos: el total del HUD baja y se sostiene <see cref="VillagerLossSeconds"/> segundos.</item>
/// </list>
/// </summary>
public sealed class GameSignals(GameData data)
{
    /// <summary>Segundos que el total de aldeanos tiene que quedar abajo para darlos por perdidos.</summary>
    public const double VillagerLossSeconds = 3;

    /// <summary>Segundos de margen entre el clic propio y la caída de un renglón para saber que es el propio.</summary>
    private const double OwnClickWindow = 4;

    private static readonly (string Tech, string Kind, string Name)[] Ages =
    [
        ("feudal_age", "rival_feudal_click", "Feudal"),
        ("castle_age", "rival_castle_click", "Castillos"),
        ("imperial_age", "rival_imperial_click", "Imperial"),
    ];

    /// <summary>
    /// Un jugador del panel. Se lo sigue por la continuidad de su puntaje, con el color como desempate: el color del
    /// texto se confunde por tramos (en la 20261019-202635 los dos renglones salieron naranja un rato, y la serie
    /// "del naranja" pasaba de 379 a 337, como una caída). Su número es el color que más se leyó.
    /// </summary>
    private sealed class Player
    {
        public int Last;
        public int? Previous;
        public double Time;
        public readonly int[] Votes = new int[9];
        public int NextAge; // índice en Ages
        public (int Before, int Expected, double Time)? Pending;
        public bool Me;

        public int Slot => Enumerable.Range(1, 8).MaxBy(s => Votes[s]) is var best && Votes[best] > 0 ? best : 0;
    }

    /// <summary>Lo que se suma a la distancia entre puntajes si el color leído no es el del jugador.</summary>
    private const int ColorMismatch = 60;

    /// <summary>
    /// Distancia de puntaje a partir de la que un renglón es otro jugador, más <see cref="ScorePerSecond"/> por cada
    /// segundo desde su última lectura (el puntaje sube unos pocos puntos por segundo).
    /// </summary>
    private const int NewPlayerDistance = 250;

    private const int ScorePerSecond = 5;

    private readonly List<GameSignal> _signals = [];
    private readonly List<Player> _players = [];
    private readonly List<double> _ownClicks = [];
    private int? _villagerPeak;
    private (int Total, double Since)? _villagerDrop;
    private int _seenEvents;

    /// <summary>El número del jugador (por su color), si se sabe (pantalla de carga o aprendido de su clic).</summary>
    public int? PlayerSlot { get; set; }

    /// <summary>Todas las señales vistas en la partida, en orden.</summary>
    public IReadOnlyList<GameSignal> All => _signals;

    /// <summary>El jugador hizo clic en una edad (lo detecta el coach por la baja del stock): su renglón cae a la vez.</summary>
    public void OwnAgeClick(double time) => _ownClicks.Add(time);

    /// <summary>Las señales nuevas de este snapshot.</summary>
    public IReadOnlyList<GameSignal> Update(GameSnapshot snapshot, PlayerState state)
    {
        var before = _signals.Count;
        if (snapshot.Scores is { } scores)
            Scores(snapshot.Time, scores);
        Attack(state);
        Villagers(snapshot.Time, state.Villagers);
        return _signals.Skip(before).ToList();
    }

    private void Scores(double time, IReadOnlyList<PlayerScore> scores)
    {
        var assigned = Assign(scores, time);
        for (var i = 0; i < scores.Count; i++)
        {
            var player = assigned[i];
            if (player is null)
            {
                player = new Player { Last = scores[i].Score, Time = time };
                _players.Add(player);
            }
            player.Votes[scores[i].Slot]++;
            Track(player, scores[i], time);
        }
    }

    /// <summary>
    /// Qué jugador es cada renglón: la asignación de menor costo total (diferencia de puntaje más lo que cuesta un
    /// color distinto), no renglón por renglón. En la 20261018-132830 el rival hizo clic (942 → 892) en la misma
    /// lectura en que el usuario llegó a Feudal (801 → 907): de a uno, el 907 quedaba con el rival y se cruzaban.
    /// null = un jugador nuevo.
    /// </summary>
    private Player?[] Assign(IReadOnlyList<PlayerScore> rows, double time)
    {
        double Cost(PlayerScore row, Player p) => Math.Abs(row.Score - p.Last) + (p.Slot != 0 && p.Slot != row.Slot ? ColorMismatch : 0);
        bool Fits(PlayerScore row, Player p) => Cost(row, p) <= NewPlayerDistance + (time - p.Time) * ScorePerSecond;
        const double NewPlayerCost = 1000;

        var best = new Player?[rows.Count];
        var current = new Player?[rows.Count];
        var bestCost = double.MaxValue;
        var used = new HashSet<Player>();
        void Search(int i, double cost)
        {
            if (cost >= bestCost)
                return;
            if (i == rows.Count)
            {
                (bestCost, best) = (cost, current.ToArray());
                return;
            }
            foreach (var p in _players.Where(p => !used.Contains(p) && Fits(rows[i], p)))
            {
                used.Add(p);
                current[i] = p;
                Search(i + 1, cost + Cost(rows[i], p));
                used.Remove(p);
            }
            current[i] = null;
            Search(i + 1, cost + NewPlayerCost);
        }
        Search(0, 0);
        return best;
    }

    private void Track(Player player, PlayerScore row, double time)
    {
        if (row.DarkAge)
            player.NextAge = 0;
        else if (player.NextAge == 0)
            player.NextAge = 1; // ya no está en la Oscura: lo próximo es Castillos

        var age = player.NextAge;
        if (player.Pending is { } pending)
        {
            player.Pending = null;
            // Confirmada si sigue abajo (una lectura mala vuelve enseguida).
            if (row.Score <= pending.Before - pending.Expected / 2 && age < Ages.Length)
                Clicked(player, pending.Time, age);
        }
        // El puntaje de antes tiene que estar firme: dos lecturas parecidas.
        else if (time - player.Time <= 6 && age < Ages.Length && player.Previous is { } previous && Math.Abs(player.Last - previous) <= 15
            && Expected(age) is var expected && player.Last - row.Score is var drop
            && drop >= expected * 0.6 && drop <= expected * 1.3 + 10)
        {
            player.Pending = (player.Last, expected, time);
        }
        (player.Previous, player.Last, player.Time) = (player.Last, row.Score, time);
    }

    private void Clicked(Player player, double time, int age)
    {
        player.NextAge = age + 1;
        if (player.Me || (PlayerSlot is { } mine && player.Slot == mine) || _ownClicks.Any(t => Math.Abs(t - time) <= OwnClickWindow))
        {
            player.Me = true;
            PlayerSlot ??= player.Slot;
            return;
        }
        var (tech, kind, name) = Ages[age];
        var arrives = data.TryGet(tech, out var entity) ? $": llega a las {GameClock.Format(time + entity.Time)}" : "";
        _signals.Add(new GameSignal(kind, time, $"El rival hizo clic en {name} ({GameClock.Format(time)}){arrives}"));
    }

    /// <summary>Lo que cae el puntaje al pagar esa edad: la décima parte de su costo (sin bonificaciones de civ).</summary>
    private int Expected(int age)
    {
        var cost = data.TryGet(Ages[age].Tech, out var entity) ? entity.Cost : ResourceAmounts.Zero;
        return (int)Math.Round((cost.Food + cost.Wood + cost.Gold + cost.Stone) / 10);
    }

    private void Attack(PlayerState state)
    {
        for (; _seenEvents < state.Events.Count; _seenEvents++)
        {
            var e = state.Events[_seenEvents];
            if (e.EventId == "under_attack" && !_signals.Any(s => s.Kind == "under_attack" && e.Time - s.Time < 30))
                _signals.Add(new GameSignal("under_attack", e.Time, "¡Te están atacando!"));
        }
    }

    private void Villagers(double time, int? total)
    {
        if (total is not { } now)
            return;
        if (_villagerPeak is not { } peak || now >= peak)
        {
            (_villagerPeak, _villagerDrop) = (now, null);
            return;
        }
        if (_villagerDrop is not { } drop || drop.Total != now)
            _villagerDrop = (now, time);
        else if (time - drop.Since >= VillagerLossSeconds)
        {
            var lost = peak - now;
            _signals.Add(new GameSignal("villagers_lost", time, $"Perdiste {lost} aldeano{(lost > 1 ? "s" : "")}: reponelos"));
            (_villagerPeak, _villagerDrop) = (now, null);
        }
    }
}
