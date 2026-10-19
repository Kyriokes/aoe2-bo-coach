using BoCoach.Core.Data;

namespace BoCoach.Core.Economy;

/// <summary>
/// La guía dinámica (GD): dónde tendrían que estar los aldeanos ahora. Parte de un reparto (el que muestra el HUD si
/// se lee, si no el de la BO) y, si con ese reparto algo va a faltar en el horizonte (lo que pide la BO hasta el
/// próximo hito, contra el stock y lo que se va a juntar), pasa aldeanos desde los recursos que sobran hasta cubrirlo.
/// Si no va a faltar nada, no cambia nada.
/// </summary>
public static class GuideDistribution
{
    /// <param name="bo">De dónde se parte: los aldeanos por recurso del HUD, o el reparto de la BO para el total de ahora.</param>
    /// <param name="demand">Lo que pide la BO por segundo hasta el próximo hito.</param>
    /// <param name="perVillager">Lo que junta por segundo un aldeano en cada recurso.</param>
    /// <param name="horizon">Segundos que se miran hacia adelante.</param>
    /// <param name="foodFloor">Aldeanos en comida que mantienen los centros urbanos produciendo: nunca se sacan de ahí.</param>
    /// <param name="shortage">Faltante a partir del cual un recurso "va a faltar".</param>
    /// <param name="maxMoves">Lo más que la GD se aleja del reparto de la BO.</param>
    /// <param name="keep">
    /// Cómo quiere la BO a los aldeanos al terminar el paso actual: de un recurso no se sacan aldeanos si quedaría
    /// debajo de eso (solo se mueve lo que sobra). En la 20261018-132830, con 6 en comida y 3 en madera y el paso
    /// pidiendo 4 en madera, la GD pedía pasar la madera a comida porque con 100 de stock "no faltaba".
    /// </param>
    public static ResourceAmounts Adjust(
        ResourceAmounts bo, ResourceAmounts stock, ResourceAmounts demand, ResourceAmounts perVillager,
        double horizon, int foodFloor, double shortage, int maxMoves, ResourceAmounts? keep = null)
    {
        var resources = Enum.GetValues<Resource>();
        var x = resources.ToDictionary(r => r, r => (int)Math.Round(bo[r]));
        double Deficit(Resource r, int villagers) => demand[r] * horizon - stock[r] - villagers * perVillager[r] * horizon;
        int Min(Resource r) => Math.Max(r == Resource.Food ? foodFloor : 0, keep is { } k ? (int)Math.Round(k[r]) : 0);

        for (var moves = 0; moves < maxMoves; moves++)
        {
            // El que más falta, medido en aldeanos que harían falta para cubrirlo.
            var needy = resources
                .Where(r => perVillager[r] > 0 && Deficit(r, x[r]) > shortage)
                .OrderByDescending(r => Deficit(r, x[r]) / (perVillager[r] * horizon))
                .Cast<Resource?>()
                .FirstOrDefault();
            if (needy is not { } to)
                break;
            // El que más sobra, siempre que sacarle uno no lo haga faltar a él.
            var donor = resources
                .Where(r => r != to && x[r] > Min(r) && Deficit(r, x[r] - 1) <= 0)
                .OrderBy(r => Deficit(r, x[r] - 1))
                .Cast<Resource?>()
                .FirstOrDefault();
            if (donor is not { } from)
                break;
            x[from]--;
            x[to]++;
        }
        return new ResourceAmounts(x[Resource.Food], x[Resource.Wood], x[Resource.Stone], x[Resource.Gold]);
    }
}
