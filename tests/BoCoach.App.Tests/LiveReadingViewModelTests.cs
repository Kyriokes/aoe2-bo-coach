using BoCoach.App.Coaching;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Loading;

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

    // Spec 2026-10-30: el panel muestra los mismos renglones que el overlay.
    [Fact]
    public void El_panel_muestra_las_lineas()
    {
        var view = new Coach(Scouts, _data.GameData, _data.Events).Update(new GameSnapshot(30, PlanStart.Standard.Stock, 4, 5));
        var hud = new HudState(RealTime: 1, Clock: 30, 200, 200, 100, 200, 3, 0, Age.Dark, [], []);
        var panel = new LiveReadingViewModel();

        panel.Apply(Frame(view, hud));

        Assert.Equal(CoachText.LaneLines(view, _data.GameData).Select(l => $"{l.Label}: {l.Text}"), panel.Lanes);
    }

    [Fact]
    public void El_panel_dice_contra_quien_se_juega_segun_la_pantalla_de_carga()
    {
        var view = new Coach(Scouts, _data.GameData, _data.Events).View;
        var hud = new HudState(RealTime: 1, Clock: null, null, null, null, null, null, null, null, [], []);
        LoadingPlayer Player(int slot, string name, string civ, int team) => new(slot, name, civ, civ, team, (0, 0, 0));
        var loading = new LoadingReading([Player(1, "Ferrus", "incas", 1), Player(2, "Arturo", "britons", 2), Player(3, "Halcon", "franks", 1)], "Arabia");
        var panel = new LiveReadingViewModel();

        panel.Apply(Frame(view, hud));
        Assert.Equal("", panel.Game);

        panel.Apply(Frame(view, hud) with { Loading = loading, Me = loading.Players[0], Civ = "incas" });
        Assert.Equal("Partida (pantalla de carga): vos Ferrus (incas) · aliados Halcon (franks) · rivales Arturo (britons) · mapa Arabia", panel.Game);

        panel.Apply(Frame(view, hud) with { Loading = loading });
        Assert.Contains("No sé cuál sos", panel.Game);
    }
}
