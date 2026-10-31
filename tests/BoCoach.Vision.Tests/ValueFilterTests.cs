using BoCoach.Vision.Smoothing;

namespace BoCoach.Vision.Tests;

public class ValueFilterTests
{
    private static double? Feed(ValueFilter filter, params double?[] readings)
    {
        double? last = null;
        for (var i = 0; i < readings.Length; i++)
            last = filter.Add(readings[i], i * 0.5);
        return last;
    }

    [Fact]
    public void Ignora_las_lecturas_que_no_sirven()
    {
        var filter = new ValueFilter();

        Assert.Equal(100, Feed(filter, 100, null, null, 100));
    }

    [Fact]
    public void Una_lectura_suelta_absurda_no_mueve_el_valor()
    {
        var filter = new ValueFilter { MaxRisePerSecond = 120, Slack = 20 };

        // "150" leído una vez como "750" (un 1 confundido con un 7).
        Assert.Equal(160, Feed(filter, 150, 155, 750, 160, 160));
    }

    [Fact]
    public void Un_salto_real_se_acepta_cuando_se_repite()
    {
        var filter = new ValueFilter { MaxRisePerSecond = 120, Slack = 20, ConfirmJump = 3 };

        // Vendió en el mercado: el oro sube de 100 a 600 y se queda ahí.
        Assert.Equal(100, Feed(filter, 100, 100, 600, 600));
        Assert.Equal(600, filter.Add(600, 2.0));
    }

    [Fact]
    public void La_mediana_saca_el_ruido()
    {
        var filter = new ValueFilter { Window = 5 };

        Assert.Equal(200, Feed(filter, 200, 200, 208, 200, 200));
    }

    [Fact]
    public void El_reloj_nunca_va_para_atras_ni_salta()
    {
        // A velocidad normal (1,7) el reloj avanza menos de 1 s por cada 0,5 s real.
        var clock = new ValueFilter { Window = 1, MaxRisePerSecond = 3, MaxDropPerSecond = 0, Slack = 1 };

        Assert.Equal(2125, Feed(clock, 2122, 2123, 2112, 2124, 2154, 2125));
    }

    [Fact]
    public void El_reloj_se_destraba_si_el_juego_se_pone_al_dia_de_golpe()
    {
        // Grabación 20261017-002121: tras un tirón el reloj pasó de 2:24 a 2:28 en medio segundo real y
        // después siguió avanzando normal; antes quedaba clavado en 2:24 el resto de la partida.
        var clock = new ValueFilter { Window = 1, MaxRisePerSecond = 3, MaxDropPerSecond = 0, Slack = 1.5, ConfirmJump = 6 };

        Assert.Equal(144, Feed(clock, 144, 148, 149));
        Assert.Equal(150, clock.Add(150, 1.5));
        Assert.Equal(151, clock.Add(151, 2.0));
    }

    [Fact]
    public void El_reloj_nunca_acepta_un_salto_de_horas_aunque_se_repita()
    {
        // Partida 20261031-140712: la letra del reloj del mod leyó "09:06:10" en vez de "00:06:10" y, con el juego en
        // pausa, la misma lectura se repitió: el reloj del coach saltó 9 horas y la partida quedó corrida.
        var clock = ValueFilter.GameClock();

        Assert.Equal(370, Feed(clock, 369, 370));
        for (var i = 0; i < 12; i++)
            Assert.Equal(370, clock.Add(32770, 1.0 + i * 0.5));
        Assert.Equal(371, clock.Add(371, 7.5));
    }

    [Fact]
    public void Las_bajas_por_gasto_se_aceptan_al_instante()
    {
        var filter = new ValueFilter { Window = 1, MaxRisePerSecond = 120, Slack = 20 };

        // Click en Feudal: la comida baja 500 de golpe.
        Assert.Equal(20, Feed(filter, 515, 520, 20));
    }
}
