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
    public void Las_bajas_por_gasto_se_aceptan_al_instante()
    {
        var filter = new ValueFilter { Window = 1, MaxRisePerSecond = 120, Slack = 20 };

        // Click en Feudal: la comida baja 500 de golpe.
        Assert.Equal(20, Feed(filter, 515, 520, 20));
    }
}
