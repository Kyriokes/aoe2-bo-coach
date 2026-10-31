using BoCoach.App.Coaching;

namespace BoCoach.App.Tests;

/// <summary>D6: un reloj que vuelve a empezar sin pantalla de carga es otra partida; una lectura equivocada no.</summary>
public class NewGameDetectorTests
{
    private static bool Any(NewGameDetector detector, params (double Raw, double Smoothed)[] reads) =>
        reads.Select(r => detector.Check(r.Raw, r.Smoothed)).ToList().Any(x => x);

    [Fact]
    public void Una_lectura_de_horas_no_hace_creer_que_empezo_otra_partida()
    {
        // Partida 20261031-140712: "09:06:10" por "00:06:10" en una captura; las siguientes, bien. Comparando con la
        // lectura cruda anterior, 6:11, 6:12 y 6:13 eran "9 horas para atrás" y el coach borraba todo (el telar hecho).
        Assert.False(Any(new NewGameDetector(), (369, 369), (370, 370), (32770, 370), (371, 371), (372, 372), (373, 373)));
    }

    [Fact]
    public void El_reloj_que_vuelve_a_empezar_es_otra_partida()
    {
        var detector = new NewGameDetector();
        Assert.False(Any(detector, (1800, 1800), (5, 1800), (6, 1800)));
        Assert.True(detector.Check(7, 1800));
    }

    [Fact]
    public void Una_sola_lectura_para_atras_no_alcanza()
    {
        Assert.False(Any(new NewGameDetector(), (1800, 1800), (5, 1800), (1801, 1801), (6, 1801), (1802, 1802)));
    }
}
