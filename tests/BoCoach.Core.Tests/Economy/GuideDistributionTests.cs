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
    public void No_saca_aldeanos_de_lo_que_el_paso_quiere_al_terminar()
    {
        // Partida 20261018-132830, 2:35: 6 en comida y 3 en madera, el paso pide 4 en madera y 6 en comida. Falta comida
        // para seguir sacando aldeanos y a la madera le sobra stock, pero sacarla contradice al paso.
        var hay = new ResourceAmounts(6, 3, 0, 0);
        var stock = new ResourceAmounts(25, 100, 200, 100);
        var demand = new ResourceAmounts(4, 0.5, 0, 0);

        Assert.True(Guide(hay, stock, demand).Wood < 3);   // sin la meta, la saca
        var guided = GuideDistribution.Adjust(hay, stock, demand, PerVillager, horizon: 60, foodFloor: 0, shortage: 25, maxMoves: 5,
            keep: new ResourceAmounts(6, 4, 0, 0));
        Assert.Equal(hay, guided);

        // Lo que sobra sobre la meta sí se puede mover.
        var extra = GuideDistribution.Adjust(new ResourceAmounts(6, 6, 0, 0), stock, demand, PerVillager, horizon: 60, foodFloor: 0,
            shortage: 25, maxMoves: 5, keep: new ResourceAmounts(6, 4, 0, 0));
        Assert.Equal(new ResourceAmounts(7, 5, 0, 0), extra);
    }

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
    public void Desde_el_reparto_real_no_pide_comida_si_sobra()
    {
        // Partida 20261018-001030, 15:11: 500 de comida y 37 de madera, y la GD pedía pasar 8 de madera a comida
        // porque partía del reparto de la BO (14 en comida). Desde lo que dice el HUD no falta nada: queda igual.
        var actual = new ResourceAmounts(Food: 5, Wood: 16, Stone: 0, Gold: 6);

        var guide = Guide(actual, stock: new(Food: 500, Wood: 37, Stone: 200, Gold: 381), demand: new(Food: 2, Wood: 1, Stone: 0, Gold: 2));

        Assert.Equal(actual, guide);
    }

    [Fact]
    public void Desde_el_reparto_real_pasa_aldeanos_solo_si_algo_va_a_faltar()
    {
        // Mucha comida y nada de madera, con la BO pidiendo madera: pasa de comida a madera.
        var actual = new ResourceAmounts(Food: 12, Wood: 2, Stone: 0, Gold: 0);

        var guide = Guide(actual, stock: new(Food: 600, Wood: 0, Stone: 0, Gold: 0), demand: new(Food: 1, Wood: 4, Stone: 0, Gold: 0));

        Assert.True(guide.Wood > actual.Wood);
        Assert.True(guide.Food < actual.Food);
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
