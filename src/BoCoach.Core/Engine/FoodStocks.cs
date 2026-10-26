using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// D12: lo que le queda a cada fuente de comida durante una simulación con la comida finita (ver
/// <see cref="FoodSupply"/>). Los aldeanos de una fuente agotada no juntan nada hasta que un paso los mueve.
/// </summary>
internal sealed class FoodStocks(FoodSupply supply)
{
    private readonly Dictionary<GatherSource, double> _pools = FoodSupply.Counted.ToDictionary(s => s, s => supply.Pools.GetValueOrDefault(s));

    /// <summary>Si la fuente se cuenta (se agota); las demás no se acaban.</summary>
    public static bool Counts(GatherSource source) => FoodSupply.Counted.Contains(source);

    /// <summary>Lo que le queda a la fuente.</summary>
    public double Remaining(GatherSource source) => _pools.GetValueOrDefault(source);

    /// <summary>Cuántos de esos aldeanos no juntan nada este segundo (la fuente se acabó).</summary>
    public int Idle(GatherSource source, int villagers) => _pools[source] > 0 ? 0 : villagers;

    /// <summary>
    /// Lo que juntan este segundo los aldeanos de la fuente, descontado de lo que queda: <paramref name="wanted"/> (lo
    /// que juntarían con la comida infinita) o lo que quede. Si alcanza, devuelve exactamente <paramref name="wanted"/>,
    /// así con comida de sobra los números son los mismos que sin contarla.
    /// </summary>
    public double Gather(GatherSource source, int villagers, double wanted)
    {
        if (villagers == 0)
            return 0;
        var gathered = Math.Min(_pools[source], wanted);
        _pools[source] -= gathered;
        return gathered;
    }
}
