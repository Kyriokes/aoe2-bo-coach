using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Tests.Economy;

public class GuideDistributionTests
{
    // Un aldeano junta 0,5 por segundo de cualquier cosa; se mira 60 s adelante: cada aldeano son 30 en el horizonte.
    private static readonly ResourceAmounts PerVillager = new(0.5, 0.5, 0.5, 0.5);

    private static ResourceAmounts Guide(ResourceAmounts bo, ResourceAmounts stock, ResourceAmounts demand, int foodFloor = 0) =>
        GuideDistribution.Adjust(bo, stock, demand, PerVillager, horizon: 60, foodFloor, shortage: 25, maxMoves: 5);

    [Fact]
    public void Si_no_va_a_faltar_nada_es_el_reparto_de_la_BO()
    {
        var bo = new ResourceAmounts(Food: 6, Wood: 4, Stone: 0, Gold: 0);

        var guide = Guide(bo, stock: new(100, 100, 0, 0), demand: new(3, 2, 0, 0));

        Assert.Equal(bo, guide);
    }

    [Fact]
    public void Si_sobra_madera_y_falta_comida_pasa_aldeanos_de_madera_a_comida()
    {
        // El jugador juntó madera de más: 600 de madera y nada de comida, con la BO pidiendo comida para Feudal.
        var bo = new ResourceAmounts(Food: 6, Wood: 4, Stone: 0, Gold: 0);

        var guide = Guide(bo, stock: new(Food: 0, Wood: 600, Stone: 0, Gold: 0), demand: new(Food: 5, Wood: 1, Stone: 0, Gold: 0));

        Assert.True(guide.Food > bo.Food);
        Assert.True(guide.Wood < bo.Wood);
        Assert.Equal(10, guide.Food + guide.Wood + guide.Gold + guide.Stone);
    }

    [Fact]
    public void Nunca_deja_corto_al_recurso_que_da_aldeanos()
    {
        // Falta comida, pero la madera justo alcanza: sacarle uno la haría faltar.
        var bo = new ResourceAmounts(Food: 6, Wood: 4, Stone: 0, Gold: 0);

        var guide = Guide(bo, stock: new(0, 0, 0, 0), demand: new(Food: 5, Wood: 2, Stone: 0, Gold: 0));

        Assert.Equal(4, guide.Wood);
    }

    [Fact]
    public void No_saca_de_comida_los_aldeanos_que_mantienen_al_centro_urbano()
    {
        // Falta madera y sobra comida, pero 6 de comida son el mínimo para no parar el centro urbano.
        var bo = new ResourceAmounts(Food: 6, Wood: 4, Stone: 0, Gold: 0);

        var guide = Guide(bo, stock: new(Food: 900, Wood: 0, Stone: 0, Gold: 0), demand: new(Food: 0, Wood: 5, Stone: 0, Gold: 0), foodFloor: 6);

        Assert.Equal(6, guide.Food);
    }
}
