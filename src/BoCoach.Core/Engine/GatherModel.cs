using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo que junta por segundo un aldeano en cada fuente, contando los viajes: llena su carga C a la velocidad de trabajo
/// w y la lleva al depósito (ida y vuelta, T segundos). Tasa = C / (C/w + T).
/// <para>La eficiencia del Planner (cuánto de la tasa del .dat se logra en partida) se lee como caminata: con los
/// valores base, e = (C/w) / (C/w + T), así que sin mejoras la tasa es la de siempre (w · e). Con mejoras, la carga
/// (carretilla) rinde más donde se camina más, una mejora de trabajo (hachas) se diluye con la caminata y una de
/// velocidad acorta T.</para>
/// </summary>
public sealed class GatherModel(GameData data, IReadOnlyDictionary<GatherSource, double> efficiency)
{
    /// <param name="researched">Tecnologías terminadas (claves de game_data), en el orden en que se investigaron.</param>
    public double Rate(GatherSource source, CivBonuses civ, Age age, IEnumerable<string> researched)
    {
        var baseWork = data.GatherRates[source];
        var baseCarry = data.Carry(source);
        var e = Math.Clamp(efficiency.GetValueOrDefault(source, 1), 0.05, 1);
        var baseWalk = baseCarry / baseWork * (1 / e - 1);

        var work = baseWork * civ.GatherMultiplier(source, age);
        var carry = civ.Carry(source, age) ?? baseCarry;
        var speed = civ.VillagerSpeed(age);
        foreach (var tech in researched)
        {
            if (!data.TryGet(tech, out var entity) || !entity.Gather.TryGetValue(source, out var effect))
                continue;
            work *= effect.Work;
            carry = carry * effect.CarryMultiplier + effect.CarryAdd;
            speed *= effect.Speed;
        }
        return carry / (carry / work + baseWalk / speed);
    }

    /// <summary>La tasa de cada fuente (ver <see cref="Rate"/>).</summary>
    public Dictionary<GatherSource, double> Rates(CivBonuses civ, Age age, IReadOnlyCollection<string> researched) =>
        Enum.GetValues<GatherSource>().ToDictionary(s => s, s => Rate(s, civ, age, researched));
}
