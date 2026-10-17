using BoCoach.Core.Data;

namespace BoCoach.Core.Economy;

/// <summary>
/// La guía dinámica (GD): dónde tendrían que estar los aldeanos ahora para volver a la BO por el camino más corto.
/// Parte del reparto de la BO y, si con ese reparto algo va a faltar en el horizonte (lo que pide la BO hasta el
/// próximo hito, contra el stock y lo que se va a juntar), pasa aldeanos desde los recursos que sobran hasta cubrirlo.
/// Cuando el stock se pone al día, el faltante desaparece y la GD vuelve sola al reparto de la BO.
/// </summary>
public static class GuideDistribution
{
    /// <param name="bo">Reparto de la BO para el total de aldeanos de ahora.</param>
    /// <param name="demand">Lo que pide la BO por segundo hasta el próximo hito.</param>
    /// <param name="perVillager">Lo que junta por segundo un aldeano en cada recurso.</param>
    /// <param name="horizon">Segundos que se miran hacia adelante.</param>
    /// <param name="foodFloor">Aldeanos en comida que mantienen los centros urbanos produciendo: nunca se sacan de ahí.</param>
    /// <param name="shortage">Faltante a partir del cual un recurso "va a faltar".</param>
    /// <param name="maxMoves">Lo más que la GD se aleja del reparto de la BO.</param>
    public static ResourceAmounts Adjust(
        ResourceAmounts bo, ResourceAmounts stock, ResourceAmounts demand, ResourceAmounts perVillager,
        double horizon, int foodFloor, double shortage, int maxMoves)
    {
        var resources = Enum.GetValues<Resource>();
        var x = resources.ToDictionary(r => r, r => (int)Math.Round(bo[r]));
        double Deficit(Resource r, int villagers) => demand[r] * horizon - stock[r] - villagers * perVillager[r] * horizon;
        int Min(Resource r) => r == Resource.Food ? foodFloor : 0;

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
