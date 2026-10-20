using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Recording;

namespace BoCoach.App.Tests;

public sealed class CoachSessionTests : IDisposable
{
    private readonly DirectoryInfo _library = Directory.CreateTempSubdirectory("bocoach-coach-");
    private readonly CoachData _data;

    public CoachSessionTests()
    {
        _data = CoachData.Load(_library.FullName);
    }

    public void Dispose() => _library.Delete(recursive: true);

    // La BO de scouts vieja (a mano, con horarios objetivo): los tests miran el desvío contra esos horarios.
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-214512");

    private sealed class FakeHost : ICoachHost
    {
        public OverlayViewModel? Shown { get; private set; }
        public bool Visible { get; private set; }
        public bool Closed { get; private set; }

        public void ShowOverlay(OverlayViewModel overlay, PixelRect area) => (Shown, Visible, Closed) = (overlay, true, false);
        public void PlaceOverlay(PixelRect area) { }
        public void SetOverlayVisible(bool visible) => Visible = visible;
        public void CloseOverlay() => Closed = true;
        public void RunOnUi(Action action) => action();
    }

    private CoachFrame Frame(CoachView view, double withoutClock = 0, bool ended = false, HudState? hud = null) =>
        new(view, hud, withoutClock, 30, new ProcessUsage(1.5, 80), false, ended, null, new PixelRect(0, 0, 1920, 1080));

    [Fact]
    public void Recordatorio_de_casa_junto_al_mouse_salvo_que_este_apagado_o_no_se_vea_el_reloj()
    {
        var coach = new Coach(Scouts, _data.GameData, _data.Events);
        var view = coach.Update(new GameSnapshot(300, new ResourceAmounts(100, 200, 200, 100), 23, 25) { Villagers = 22 });
        var overlay = new OverlayViewModel(_data.GameData, Scouts.Name);

        overlay.Apply(Frame(view));
        Assert.True(overlay.HouseReminder);
        Assert.True(overlay.ShowCursorReminder);

        overlay.CursorReminders = false;
        Assert.False(overlay.ShowCursorReminder);

        overlay.CursorReminders = true;
        overlay.Apply(Frame(view, withoutClock: 10));
        Assert.False(overlay.ShowCursorReminder);
    }

    [Fact]
    public void Los_recordatorios_en_el_mouse_se_guardan_con_los_ajustes()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        var session = new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path);
        Assert.True(session.CursorReminders);

        session.ToggleCursorRemindersCommand.Execute(null);

        Assert.False(CoachSettings.Load(path).CursorReminders);
        Assert.False(new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path).CursorReminders);
    }

    // Partida 20261017-163738 (incas): el coach tomaba la civ de la BO ("any") y no aplicaba las bonificaciones
    // de la civ con que se juega. La civ de la partida se elige en la ventana y queda guardada.
    [Fact]
    public void La_civ_de_la_partida_se_elige_en_la_ventana_se_guarda_y_el_coach_la_usa_en_vez_de_la_de_la_BO()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path);
        Assert.Equal(CoachSessionViewModel.BoCiv, session.GameCiv);
        Assert.Contains("incas", session.CivOptions);

        session.GameCiv = "incas";
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));

        Assert.Equal("incas", session.LiveCoach!.BuildOrder.Civ);
        Assert.Equal("incas", CoachSettings.Load(path).GameCiv);
        Assert.Equal("incas", new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path).GameCiv);

        session.GameCiv = CoachSessionViewModel.BoCiv;
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
        Assert.Equal(Scouts.Civ, session.LiveCoach!.BuildOrder.Civ);
    }

    [Fact]
    public void El_overlay_muestra_el_paso_actual_lo_que_esta_en_curso_y_el_objetivo()
    {
        var coach = new Coach(Scouts, _data.GameData, _data.Events);
        coach.Update(new GameSnapshot(520, new ResourceAmounts(523, 200, 200, 100), 22, 25) { Villagers = 21 });
        var view = coach.Update(new GameSnapshot(520.5, new ResourceAmounts(33, 200, 200, 100), 22, 25) { Villagers = 21 });
        var overlay = new OverlayViewModel(_data.GameData, Scouts.Name);

        overlay.Apply(Frame(view));

        Assert.Equal("8:40", overlay.Clock);
        Assert.Contains(overlay.Running, l => l.StartsWith("En curso: Edad Feudal"));
        Assert.Equal("Clic de Feudal a las 8:40, objetivo 9:00", overlay.Deviation);
        Assert.False(overlay.Behind);
        Assert.Null(overlay.Warning);
        Assert.Equal($"{view.Current!.Index + 1}/{Scouts.Steps.Count}", overlay.Progress);
        Assert.Contains("CPU 1.5 %", overlay.Debug.Replace(',', '.'));
    }

    [Fact]
    public void Avisa_si_no_ve_el_reloj_si_un_numero_no_entra_y_cuando_termina_la_grabacion()
    {
        var view = new Coach(Scouts, _data.GameData, _data.Events).View;
        var overlay = new OverlayViewModel(_data.GameData, Scouts.Name);
        var hud = new HudState(10, 1, 200, 200, 100, 200, 3, 0, Age.Dark, [], ["food"]);

        overlay.Apply(Frame(view, withoutClock: 5));
        Assert.Contains("reloj", overlay.Warning);

        overlay.Apply(Frame(view, hud: hud));
        Assert.Contains("comida", overlay.Warning);

        overlay.Apply(Frame(view, ended: true));
        Assert.Equal("Terminó la grabación.", overlay.Warning);
    }

    // B4 (b): con el HUD a otra escala las franjas se miden en la captura llevada a 1080p; la GD y el aviso de
    // inactivos se dibujan en la pantalla, así que vuelven al tamaño real.
    [Fact]
    public void Con_el_HUD_a_otra_escala_la_GD_va_en_la_franja_de_tamano_real()
    {
        var view = new Coach(Scouts, _data.GameData, _data.Events).View;
        var overlay = new OverlayViewModel(_data.GameData, Scouts.Name);
        var bands = new Dictionary<string, PixelRect> { ["food"] = new(2, 2, 36, 12), ["idle_villagers"] = new(1, 2, 36, 12) };
        var hud = new HudState(10, 1, 200, 200, 100, 200, 3, 1, Age.Dark, [], [], ModBands: bands);

        overlay.Apply(Frame(view, hud: hud) with { HudScale = 1.5 });

        Assert.Equal(new PixelRect(3, 3, 54, 18), overlay.Resources.Single(r => r.Region == "food").Band);
        Assert.Equal(new PixelRect(2, 3, 54, 18), overlay.IdleAlert.Band);
    }

    [Fact]
    public void Sin_BO_elegida_no_arranca_y_lo_dice()
    {
        var host = new FakeHost();
        var session = new CoachSessionViewModel(_data, host, () => null, () => throw new InvalidOperationException("no debería capturar"));

        session.StartStopCommand.Execute(null);

        Assert.False(session.IsRunning);
        Assert.Null(host.Shown);
        Assert.Contains("Elegí una build order", session.Status);
    }

    [Fact]
    public async Task Corre_una_grabacion_entera_con_el_overlay_y_se_detiene()
    {
        var host = new FakeHost();
        var session = new CoachSessionViewModel(_data, host, () => Scouts, () => throw new InvalidOperationException());
        var ended = new TaskCompletionSource();
        var frames = 0;
        session.FrameApplied += frame =>
        {
            frames++;
            if (frame.Ended)
                ended.TrySetResult();
        };

        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
        Assert.True(session.IsRunning);
        Assert.Same(session.Overlay, host.Shown);

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(frames >= 40);
        Assert.NotEqual("--:--", session.Overlay!.Clock);
        Assert.Equal("Terminó la grabación.", session.Overlay.Warning);

        session.ToggleOverlayCommand.Execute(null);
        Assert.False(host.Visible);

        session.StartStopCommand.Execute(null);
        Assert.False(session.IsRunning);
        Assert.True(host.Closed);
    }
}
