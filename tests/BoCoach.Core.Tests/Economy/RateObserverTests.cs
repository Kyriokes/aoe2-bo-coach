using BoCoach.Core.Data;
using BoCoach.Core.Economy;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Economy;

public class RateObserverTests
{
    private static ResourceAmounts Food(double f) => ResourceAmounts.Of(Resource.Food, f);

    [Fact]
    public void Tasa_constante()
    {
        var observer = new RateObserver(TestData.GameData);

        for (var t = 0; t <= 30; t++)
            observer.Add(t, Food(100 + 2 * t), []);

        Assert.Equal(2, observer.Rates.Food, 6);
        Assert.Equal(20, observer.Coverage, 6);
    }

    [Fact]
    public void El_gasto_de_un_evento_no_baja_la_tasa()
    {
        var observer = new RateObserver(TestData.GameData);
        for (var t = 0; t <= 10; t++)
            observer.Add(t, Food(100 + 2 * t), []);

        // En el segundo 11 sale un aldeano (50 de comida) y se recolectaron 2.
        observer.Add(11, Food(100 + 22 - 50), [new GameEvent("villager_created", 11)]);

        Assert.Equal(2, observer.Rates.Food, 6);
    }

    [Fact]
    public void Una_baja_sin_evento_se_toma_como_gasto_y_no_da_tasa_negativa()
    {
        var observer = new RateObserver(TestData.GameData);
        observer.Add(0, Food(600), []);

        observer.Add(1, Food(100), []); // click en Feudal, que el log no muestra

        Assert.Equal(0, observer.Rates.Food);
    }

    [Fact]
    public void La_ventana_olvida_lo_viejo()
    {
        var observer = new RateObserver(TestData.GameData, windowSeconds: 10);
        for (var t = 0; t <= 10; t++)
            observer.Add(t, Food(5 * t), []);
        for (var t = 11; t <= 30; t++)
            observer.Add(t, Food(50 + (t - 10)), []);

        Assert.Equal(1, observer.Rates.Food, 6);
    }
}
