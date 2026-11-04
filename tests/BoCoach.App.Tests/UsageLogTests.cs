using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;

namespace BoCoach.App.Tests;

/// <summary>Modo desarrollador: el consumo de cada partida en un CSV, para comparar (por ejemplo, el intervalo de captura).</summary>
public sealed class UsageLogTests : IDisposable
{
    private static readonly CoachData Data = CoachData.Load();
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-uso-");
    private double _now;
    private int _games;
    private bool _enabled = true;

    public void Dispose() => _dir.Delete(recursive: true);

    private GameUsageLog Log() => new(() => _enabled, () => Path.Combine(_dir.FullName, "sub", $"partida{++_games}.csv"), () => _now);

    private static CoachFrame Frame(double cpu, double ram, double ms)
    {
        var view = new Coach(BuildOrder.None, Data.GameData, Data.Events).Update(new GameSnapshot(0, new ResourceAmounts(200, 200, 100, 200), 4, 5));
        return new CoachFrame(view, null, 0, ms, new ProcessUsage(cpu, ram), false, false, null, new PixelRect(0, 0, 1920, 1080));
    }

    private string[] Lines(int game) => File.ReadAllLines(Path.Combine(_dir.FullName, "sub", $"partida{game}.csv"));

    [Fact]
    public void Una_partida_deja_su_csv_con_una_linea_cada_dos_segundos()
    {
        var log = Log();

        foreach (var (at, cpu) in new[] { (0.0, 1.5), (0.5, 9.0), (2.0, 2.5), (3.0, 9.0), (4.5, 3.5) })
        {
            _now = at;
            log.OnFrame(Frame(cpu, 60, 30), inGame: true);
        }
        log.OnCoachStopped();

        Assert.Equal(
            ["segundos,cpu_porcentaje,ram_mb,ms_por_captura", "0,1.50,60.0,30.0", "2,2.50,60.0,30.0", "4,3.50,60.0,30.0"],
            Lines(1));
    }

    [Fact]
    public void Fuera_de_la_partida_no_escribe_y_cada_partida_tiene_su_archivo()
    {
        var log = Log();

        log.OnFrame(Frame(1, 60, 30), inGame: false);
        Assert.Equal(0, _games);

        log.OnFrame(Frame(1, 60, 30), inGame: true);
        log.OnFrame(Frame(1, 60, 30), inGame: false);
        _now = 10;
        log.OnFrame(Frame(2, 60, 30), inGame: true);
        log.OnCoachStopped();

        Assert.Equal(2, _games);
        Assert.Equal(2, Lines(1).Length);
        Assert.Equal(2, Lines(2).Length);
    }

    [Fact]
    public void Sin_el_modo_desarrollador_no_escribe_nada()
    {
        _enabled = false;
        var log = Log();

        log.OnFrame(Frame(1, 60, 30), inGame: true);
        log.OnCoachStopped();

        Assert.Equal(0, _games);
    }

    // Una carpeta sin permiso no le corta nada a la partida.
    [Fact]
    public void Si_no_se_puede_escribir_sigue_de_largo()
    {
        var blocked = Path.Combine(_dir.FullName, "archivo");
        File.WriteAllText(blocked, "");
        var log = new GameUsageLog(() => true, () => Path.Combine(blocked, "uso.csv"), () => _now);

        log.OnFrame(Frame(1, 60, 30), inGame: true);
        log.OnCoachStopped();
    }
}
