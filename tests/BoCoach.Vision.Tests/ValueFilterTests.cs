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

    // Una primera lectura mala dejaba el reloj clavado toda la partida: "09:06:10" se aceptaba y las buenas eran una baja
    // de 9 horas, que no pasa nunca. La primera se toma recién cuando la siguiente puede seguirle.
    [Fact]
    public void Una_primera_lectura_mala_del_reloj_no_lo_deja_clavado()
    {
        var clock = ValueFilter.GameClock();

        Assert.Null(clock.Add(32770, 0));
        Assert.Null(clock.Value);
        Assert.Null(clock.Add(370, 0.5));
        Assert.Equal(371, clock.Add(371, 1));
        Assert.Equal(372, clock.Add(372, 1.5));
    }

    // Coach arrancado con el juego en pausa, o con algo tapando el reloj en las dos primeras capturas: la misma lectura
    // mala dos veces se confirmaba, y las buenas eran una baja que no pasaba nunca (el reloj quedaba clavado toda la
    // partida). Mientras el reloj no avanzó desde la primera, las buenas que avanzan entre sí la reemplazan. 3670 es
    // "01:01:10" por "00:06:10": una hora posible, que el lector no puede descartar.
    [Theory]
    [InlineData(32770)]
    [InlineData(3670)]
    public void Una_primera_lectura_mala_repetida_se_corrige_cuando_el_reloj_avanza(double bad)
    {
        var clock = ValueFilter.GameClock();

        Assert.Equal(bad, Feed(clock, bad, bad, bad, bad));
        var good = 370.0;
        for (var t = 2.0; t < 8; t += 0.5)
            clock.Add(good++, t);
        Assert.Equal(good - 1, clock.Value);
        Assert.Equal(good, clock.Add(good, 8));
    }

    // Lo de arriba solo vale al arrancar: con el reloj ya corroborado, una racha mala que avanza (un cuadro del juego
    // tapando la hora mientras la partida sigue) no lo mueve, y el reloj bueno sigue donde estaba.
    [Fact]
    public void Con_el_reloj_ya_corroborado_una_racha_mala_que_avanza_no_lo_mueve()
    {
        var clock = ValueFilter.GameClock();

        Assert.Equal(377, Feed(clock, 369, 370, 371, 372, 373, 374, 375, 376, 377));
        for (var i = 0; i < 12; i++)
            Assert.Equal(377, clock.Add(3978 + i, 4.5 + i * 0.5));
        Assert.Equal(390, clock.Add(390, 10.5));
    }

    // Al revés: el reloj bueno recién tomado, todavía sin corroborar (el coach arrancado a mitad de partida), y un cuadro
    // que tapa la hora y lee "1:01:12" por "0:06:12" mientras el juego sigue. Las lecturas malas del reloj suman horas:
    // una racha más alta no reemplaza lo aceptado, tampoco al arrancar.
    [Fact]
    public void Un_reloj_recien_tomado_no_lo_pisa_una_racha_mala_mas_alta()
    {
        var clock = ValueFilter.GameClock();

        Assert.Equal(371, Feed(clock, 370, 371));
        for (var i = 0; i < 12; i++)
            Assert.Equal(371, clock.Add(3972 + i, 1.0 + i * 0.5));
        Assert.Equal(383, clock.Add(383, 7.0));
    }

    /// <summary>
    /// Una partida vista cada 0,5 s desde las 20:00 a velocidad normal (1,7), con las capturas de <paramref name="from"/>
    /// a <paramref name="to"/> (sin incluir) leyendo una hora de más ("1:20:00" por "0:20:00", un cuadro tapando el reloj),
    /// o lo que diga <paramref name="offset"/>.
    /// </summary>
    private static IEnumerable<(double Time, double Truth, double Read)> Game(
        int from, int to, double pausedUntil = 0, double start = 1200, double offset = 3600, int length = 200)
    {
        for (var i = 0; i < length; i++)
        {
            var t = i * 0.5;
            var truth = start + Math.Floor(1.7 * Math.Max(0, t - pausedUntil));
            yield return (t, truth, i >= from && i < to ? truth + offset : truth);
        }
    }

    // Arrancando a mitad de partida con una o varias lecturas malas al principio, el reloj llega al bueno a los pocos
    // segundos de verlo, y desde ahí no se mueve. Tapado 30 s también: más de 7 capturas malas al arrancar corroboraban
    // la hora mala y el reloj quedaba clavado toda la partida.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(60)]
    public void Con_lecturas_malas_al_arrancar_el_reloj_llega_al_bueno_en_pocos_segundos(int bad)
    {
        var clock = ValueFilter.GameClock();

        foreach (var (time, truth, read) in Game(0, bad))
        {
            var value = clock.Add(read, time);
            if (time >= bad * 0.5 + 5)
                Assert.Equal(truth, value);
        }
    }

    // Lo de arriba no abre la puerta a mitad de partida: con el reloj ya corroborado, una racha más baja que no llega a
    // una hora de diferencia (un dígito de los minutos mal leído) no lo mueve.
    [Fact]
    public void Con_el_reloj_ya_corroborado_una_racha_mas_baja_por_minutos_no_lo_mueve()
    {
        var clock = ValueFilter.GameClock();

        Assert.Equal(2377, Feed(clock, 2369, 2370, 2371, 2372, 2373, 2374, 2375, 2376, 2377));
        for (var i = 0; i < 12; i++)
            Assert.Equal(2377, clock.Add(1178 + i, 4.5 + i * 0.5));
        Assert.Equal(2390, clock.Add(2390, 10.5));
    }

    // Ni a mitad de una partida de más de 50 minutos: un cuadro que lee la hora una menos ("0:05:00" por "1:05:00")
    // mientras el juego sigue no mueve el reloj, dure lo que dure. La cadena de más de 50 minutos por debajo vale solo en
    // los primeros dos minutos, que es cuando un cuadro tapa la hora al arrancar. Antes bajaba el reloj una hora y lo
    // dejaba clavado ahí: las buenas eran una subida de una hora, que no se confirma nunca.
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void A_mitad_de_partida_una_hora_menos_no_mueve_el_reloj(int bad)
    {
        var clock = ValueFilter.GameClock();

        foreach (var (time, truth, read) in Game(400, 400 + bad, start: 3900, offset: -3600, length: 600))
        {
            var value = clock.Add(read, time);
            if (time > 0)
                Assert.InRange(value!.Value, 0, truth);
            if (read == truth && time > 0)
                Assert.Equal(truth, value);
        }
    }

    // Si igual un reemplazo hacia abajo fue el equivocado (al arrancar, con el reloj bueno recién tomado o ya corroborado,
    // un cuadro que lee una hora o diez minutos menos mientras el juego sigue), las buenas que vuelven siguen desde lo
    // reemplazado: el reloj vuelve a ellas a los pocos segundos. Antes quedaba atrás toda la partida, o 7 minutos con
    // diez minutos de diferencia.
    [Theory]
    [InlineData(2122, -600, 2, 8)]
    [InlineData(3900, -3600, 2, 8)]
    [InlineData(3900, -3600, 2, 20)]
    [InlineData(3900, -3600, 100, 8)]
    [InlineData(3900, -3600, 100, 12)]
    [InlineData(3900, -3600, 100, 20)]
    [InlineData(3900, -3600, 100, 60)]
    public void Un_reemplazo_equivocado_se_deshace_cuando_vuelve_el_reloj_bueno(double start, double offset, int from, int bad)
    {
        var clock = ValueFilter.GameClock();

        foreach (var (time, truth, read) in Game(from, from + bad, start: start, offset: offset))
        {
            var value = clock.Add(read, time);
            if (time > 0)
                Assert.InRange(value!.Value, 0, truth);
            if (time >= (from + bad) * 0.5 + 5)
                Assert.Equal(truth, value);
        }
    }

    // Volver es para lo que tapó un cuadro hace poco. Si la hora mala del arranque ("1:20:00" por "0:20:00") ya se
    // corrigió y el mismo cuadro aparece minutos después, no la trae de vuelta. Si aparece antes de los dos minutos, la
    // trae mientras dura, y con el reloj bueno vuelve a la hora real.
    [Theory]
    [InlineData(100, false)]
    [InlineData(400, true)]
    public void Una_hora_mala_corregida_al_arrancar_no_queda_si_vuelve_el_mismo_cuadro(int again, bool late)
    {
        var clock = ValueFilter.GameClock();

        for (var i = 0; i < 600; i++)
        {
            var t = i * 0.5;
            var truth = 1200 + Math.Floor(1.7 * t);
            var covered = i < 8 || i >= again && i < again + 20;
            var value = clock.Add(covered ? truth + 3600 : truth, t);
            if (t < 9)
                continue;
            if (late)
                Assert.InRange(value!.Value, 0, truth);
            if (i < again || i >= again + 30)
                Assert.Equal(truth, value);
        }
    }

    // Y un reloj bueno no lo pisa una racha mala, sea cual sea su largo: ni recién tomado, ni con el coach arrancado en
    // pausa (lo aceptado queda sin corroborar hasta despausar), ni en una partida nueva desde 0:00. Mientras dura la racha
    // el reloj espera, y vuelve con la primera buena.
    [Theory]
    [InlineData(6, 0, 1200)]
    [InlineData(8, 0, 1200)]
    [InlineData(20, 0, 1200)]
    [InlineData(60, 0, 1200)]
    [InlineData(30, 60, 1200)]
    [InlineData(8, 0, 0)]
    [InlineData(20, 0, 0)]
    public void Un_reloj_bueno_no_lo_pisa_una_racha_mala(int bad, double pausedUntil, double start)
    {
        var clock = ValueFilter.GameClock();
        var from = pausedUntil > 0 ? (int)(pausedUntil / 0.5) : 2;

        foreach (var (time, truth, read) in Game(from, from + bad, pausedUntil, start))
        {
            var value = clock.Add(read, time);
            if (time > 0)
                Assert.InRange(value!.Value, 0, truth);
            if (read == truth && time > 0)
                Assert.Equal(truth, value);
        }
    }

    // Una lectura descartada suelta entre las buenas no arma una cadena: hacen falta varias seguidas.
    [Fact]
    public void Lecturas_malas_sueltas_al_arrancar_no_reemplazan_el_reloj()
    {
        var clock = ValueFilter.GameClock();

        Assert.Equal(371, Feed(clock, 370, 371));
        var t = 1.0;
        for (var i = 0; i < 8; i++)
        {
            clock.Add(3672 + i, t += 0.5);
            Assert.Equal(372 + i, clock.Add(372 + i, t += 0.5));
        }
    }

    [Fact]
    public void Dos_lecturas_seguidas_dan_el_reloj()
    {
        var clock = ValueFilter.GameClock();

        Assert.Null(clock.Add(370, 0));
        Assert.Equal(370, clock.Add(370, 0.5)); // en pausa no avanza, y da lo mismo
    }

    [Fact]
    public void Lo_que_no_es_el_reloj_no_espera_la_segunda_lectura()
    {
        Assert.Equal(100, new ValueFilter { Window = 3, MaxRisePerSecond = 150, Slack = 40 }.Add(100, 0));
    }

    [Fact]
    public void Las_bajas_por_gasto_se_aceptan_al_instante()
    {
        var filter = new ValueFilter { Window = 1, MaxRisePerSecond = 120, Slack = 20 };

        // Click en Feudal: la comida baja 500 de golpe.
        Assert.Equal(20, Feed(filter, 515, 520, 20));
    }
}
