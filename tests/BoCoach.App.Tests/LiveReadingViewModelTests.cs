using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;

namespace BoCoach.App.Tests;

public sealed class LiveReadingViewModelTests : IDisposable
{
    private readonly DirectoryInfo _library = Directory.CreateTempSubdirectory("bocoach-panel-");
    private readonly CoachData _data;

    public LiveReadingViewModelTests()
    {
        _data = CoachData.Load(_library.FullName);
    }

    public void Dispose() => _library.Delete(recursive: true);

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    private static CoachFrame Frame(CoachView view, HudState hud, double startOffset = 0) =>
        new(view, hud, 0, 30, new ProcessUsage(1, 80), false, false, null, new PixelRect(0, 0, 1920, 1080), startOffset);

    // Partida 20261017-163738: 36 minutos quieto antes de empezar. El overlay cuenta desde que empezó a jugar y el
    // reloj del juego no: el panel tiene que decirlo, si no los tiempos parecen cualquier cosa.
    [Fact]
    public void El_reloj_del_panel_dice_cuando_empezo_la_partida_si_no_fue_a_las_0_00()
    {
        var coach = new Coach(Scouts, _data.GameData, _data.Events);
        var view = coach.Update(new GameSnapshot(4, PlanStart.Standard.Stock with { Food = 150 }, 4, 5) { Villagers = 3 });
        var hud = new HudState(RealTime: 1, Clock: 2190, 150, 200, 100, 200, 3, 0, Age.Dark, [], []);
        var panel = new LiveReadingViewModel();

        panel.Apply(Frame(view, hud, startOffset: 2186));
        Assert.Contains("36:26", panel.Clock);
        Assert.Contains("0:04", panel.Clock);

        panel.Apply(Frame(view, hud));
        Assert.Equal("36:30", panel.Clock);
    }
}
