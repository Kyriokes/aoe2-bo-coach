using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// D12: lo que le queda a cada fuente de comida durante una simulación con la comida finita (ver
/// <see cref="FoodSupply"/>). Los aldeanos de una fuente agotada no juntan nada hasta que un paso los mueve. Cada
/// granjero tiene su granja: el que llega trae una llena y el que se va se lleva la última; una granja vacía espera
/// que se la resiembre.
/// </summary>
internal sealed class FoodStocks(FoodSupply supply)
{
    private readonly Dictionary<GatherSource, double> _pools = FoodSupply.Counted.ToDictionary(s => s, s => supply.Pools.GetValueOrDefault(s));

    /// <summary>Lo que le queda a la granja de cada granjero, en el orden en que llegaron.</summary>
    private readonly List<double> _farms = [];

    /// <summary>Si la fuente se cuenta (se agota); las demás no se acaban.</summary>
    public static bool Counts(GatherSource source) => source == GatherSource.Farm || FoodSupply.Counted.Contains(source);

    /// <summary>Lo que le queda a la fuente (a las granjas, entre todas).</summary>
    public double Remaining(GatherSource source) => source == GatherSource.Farm ? _farms.Sum() : _pools.GetValueOrDefault(source);

    /// <summary>Una granja por granjero: los que llegan traen una con <paramref name="farmFood"/>, los que se van se llevan la última.</summary>
    public void SyncFarms(int farmers, double farmFood)
    {
        while (_farms.Count < farmers)
            _farms.Add(farmFood);
        if (_farms.Count > farmers)
            _farms.RemoveRange(farmers, _farms.Count - farmers);
    }

    /// <summary>Resiembra las granjas vacías de a una mientras alcance la madera; devuelve cuántas.</summary>
    public int Reseed(double wood, double cost, double farmFood)
    {
        var reseeded = 0;
        for (var i = 0; i < _farms.Count; i++)
        {
            if (_farms[i] > 0)
                continue;
            if (wood < cost * (reseeded + 1))
                break;
            _farms[i] = farmFood;
            reseeded++;
        }
        return reseeded;
    }

    /// <summary>Cuántos de esos aldeanos no juntan nada este segundo (la fuente se acabó o su granja está vacía).</summary>
    public int Idle(GatherSource source, int villagers) => source == GatherSource.Farm
        ? _farms.Take(villagers).Count(f => f <= 0)
        : _pools[source] > 0 ? 0 : villagers;

    /// <summary>
    /// Lo que juntan este segundo los aldeanos de la fuente, descontado de lo que queda: <paramref name="wanted"/> (lo
    /// que juntarían con la comida infinita) o lo que quede. <paramref name="yield"/> es lo que rinde la comida de la
    /// fuente para la civ (tártaros: ovejas ×1,57): cada unidad que se junta gasta 1/rendimiento de la fuente. En las
    /// granjas (su comida ya viene con la civ) cada una da a lo sumo lo de un granjero. Si alcanza, devuelve exactamente
    /// <paramref name="wanted"/>, así con comida de sobra los números son los mismos que sin contarla.
    /// </summary>
    public double Gather(GatherSource source, int villagers, double wanted, double yield = 1)
    {
        if (villagers == 0)
            return 0;
        if (source != GatherSource.Farm)
        {
            if (_pools[source] * yield <= wanted)
            {
                var rest = _pools[source] * yield;
                _pools[source] = 0;
                return rest;
            }
            _pools[source] -= wanted / yield;
            return wanted;
        }

        var share = wanted / villagers;
        var working = Math.Min(villagers, _farms.Count);
        if (working == villagers && _farms.Take(working).All(f => f >= share))
        {
            for (var i = 0; i < working; i++)
                _farms[i] -= share;
            return wanted;
        }
        var total = 0.0;
        for (var i = 0; i < working; i++)
        {
            var given = Math.Min(_farms[i], share);
            _farms[i] -= given;
            total += given;
        }
        return total;
    }
}
