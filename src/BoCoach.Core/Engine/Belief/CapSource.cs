using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// El tope de población del HUD como fuente de la creencia (fuerza Observada): delata las casas que el log no avisó
/// (una segunda "House Built" mientras la primera sigue siendo el último renglón no cambia nada en el log). Una suba
/// que ningún edificio avisado explica es una casa terminada; si el aviso llega después, la creencia lo absorbe.
/// </summary>
internal sealed class CapSource(GameData data, CivBonuses civ)
{
    private const string House = "house";

    /// <summary>Si el tope no se leyó en este tiempo (segundos de partida), su próxima lectura es un punto de partida nuevo.</summary>
    public const double CapGapSeconds = 5;

    private readonly List<(double Time, int Pop)> _credits = [];
    private (int Cap, double Time)? _lastReadCap;

    /// <summary>Un edificio que da población terminó: su suba del tope ya está explicada.</summary>
    public void Credit(int pop, double now) => _credits.Add((now, pop));

    /// <summary>Procesa una lectura del tope y devuelve cuántas casas delata.</summary>
    public int HousesFrom(int cap, double now)
    {
        _credits.RemoveAll(c => now - c.Time > StateBelief.CreditSeconds);
        var houses = 0;
        if (_lastReadCap is { } last && now - last.Time <= CapGapSeconds && cap > last.Cap)
        {
            var rise = cap - last.Cap;
            // Primero, lo que explican los edificios avisados.
            while (rise > 0 && _credits.Count > 0)
            {
                var used = Math.Min(rise, _credits[0].Pop);
                rise -= used;
                if (used == _credits[0].Pop)
                    _credits.RemoveAt(0);
                else
                    _credits[0] = (_credits[0].Time, _credits[0].Pop - used);
            }
            // Con la civ: las casas de los incas dan 10 (sin esto, cada casa se contaba como dos).
            var housePop = data.TryGet(House, out var house) && civ.PopBonus(house, Age.Dark) > 0 ? civ.PopBonus(house, Age.Dark) : 5;
            for (; rise >= housePop; rise -= housePop)
                houses++;
        }
        _lastReadCap = (cap, now);
        return houses;
    }
}
