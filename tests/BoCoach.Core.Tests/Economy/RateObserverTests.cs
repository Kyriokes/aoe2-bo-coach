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
        var observer = new RateObserver();

        for (var t = 0; t <= 30; t++)
            observer.Add(t, Food(100 + 2 * t));

        Assert.Equal(2, observer.Rates.Food, 6);
        Assert.Equal(20, observer.Coverage, 6);
    }

    [Fact]
    public void Pagar_algo_solo_pierde_lo_juntado_en_ese_segundo()
    {
        var observer = new RateObserver();
        for (var t = 0; t <= 10; t++)
            observer.Add(t, Food(100 + 2 * t));

        // En el segundo 11 se encola un aldeano (baja 50 de comida) y se recolectaron 2.
        observer.Add(11, Food(100 + 22 - 50));

        Assert.Equal(20.0 / 11, observer.Rates.Food, 6);
    }

    // Auditoría del 2026-11-05: el juego cobra al encolar y el aviso llega cuando sale la unidad, 25 s después. Sumar el
    // costo con el aviso eran +2 de comida por segundo que nadie juntó.
    [Fact]
    public void El_aviso_de_una_unidad_no_suma_lo_que_se_pago_al_encolarla()
    {
        var observer = new RateObserver();
        var state = new PlayerState();
        for (var t = 0; t <= 20; t++)
        {
            GameEvent[] events = t == 20 ? [new GameEvent("villager_created", t)] : [];
            state.Apply(new GameSnapshot(t, Food(100 + 2 * t), 5, 10, events));
            observer.Add(state);
        }

        Assert.Equal(2, observer.Rates.Food, 6);
    }

    // El reloj se lee en segundos enteros y se mira la pantalla cada 400 o 500 ms: la mitad de las capturas caen en el
    // mismo segundo que la anterior, y lo juntado entre ellas se perdía.
    [Fact]
    public void Las_capturas_del_mismo_segundo_no_pierden_lo_juntado()
    {
        var observer = new RateObserver();

        for (var t = 0; t <= 30; t++)
        {
            observer.Add(t, Food(100 + 2 * t));
            observer.Add(t, Food(100 + 2 * t + 1));
        }

        Assert.Equal(2, observer.Rates.Food, 6);
    }

    [Fact]
    public void Si_el_reloj_vuelve_para_atras_sigue_midiendo_desde_ahi()
    {
        var observer = new RateObserver();
        observer.Add(100, Food(100));
        observer.Add(101, Food(102));

        observer.Add(50, Food(104));
        observer.Add(51, Food(106));

        Assert.Equal(2, observer.Coverage); // 100 a 101 y 50 a 51: el salto mismo no cuenta
        Assert.Equal(2, observer.Rates.Food, 6);
    }

    [Fact]
    public void Una_baja_se_toma_como_gasto_y_no_da_tasa_negativa()
    {
        var observer = new RateObserver();
        observer.Add(0, Food(600));

        observer.Add(1, Food(100)); // click en Feudal

        Assert.Equal(0, observer.Rates.Food);
    }

    [Fact]
    public void La_ventana_olvida_lo_viejo()
    {
        var observer = new RateObserver(windowSeconds: 10);
        for (var t = 0; t <= 10; t++)
            observer.Add(t, Food(5 * t));
        for (var t = 11; t <= 30; t++)
            observer.Add(t, Food(50 + (t - 10)));

        Assert.Equal(1, observer.Rates.Food, 6);
    }
}
