using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
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
        public void PlaceOverlay(PixelRect area, bool lowered = false) => Lowered = lowered;

        public bool Lowered { get; private set; }

        public (IReadOnlyList<string> Summary, string Path)? Report { get; private set; }

        public void ShowReport(IReadOnlyList<string> summary, string path) => Report = (summary, path);
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

    // Amigos con el juego en castellano (2026-10-20): el log dice "--Aldeano: creación finalizada--" y la edad "Alta
    // Edad Media". El idioma del juego se elige en la ventana, queda guardado y el coach lee con sus frases.
    [Fact]
    public void El_idioma_del_juego_se_elige_en_la_ventana_se_guarda_y_el_coach_usa_sus_frases()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path);
        Assert.Equal(GameLanguage.English, session.Language);
        Assert.Contains(GameLanguage.SpanishLatinAmerica, session.LanguageOptions);

        session.Language = GameLanguage.SpanishLatinAmerica;
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));

        Assert.Contains("Jinete: creación finalizada", session.LiveCoach!.Events.Phrases["knight_created"]);
        Assert.Contains("Knight Created", session.LiveCoach.Events.Phrases["knight_created"]);
        Assert.Equal("mx", CoachSettings.Load(path).GameLanguage);
        Assert.Equal(GameLanguage.SpanishLatinAmerica,
            new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path).Language);

        session.Language = GameLanguage.English;
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
        Assert.DoesNotContain("Jinete: creación finalizada", session.LiveCoach!.Events.Phrases["knight_created"]);
    }

    /// <summary>Una pantalla entera guardada, sin el HUD (sin reloj), como la captura en vivo.</summary>
    private sealed class StillScreen(string name) : IFrameSource
    {
        private readonly CapturedImage _screen = Png.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "screens", name));
        private int _served;

        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, _screen.Width, _screen.Height);

        public Frame? Next() => new(_ => null, ++_served * 0.5, Area, rect => _screen.Crop(rect));
    }

    // D4: en la pantalla de carga se sugiere la BO de la biblioteca que encaja (japoneses: hombres de armas o arqueros,
    // así que la de arqueros) y Ctrl+Alt+B la usa.
    [Fact]
    public async Task En_la_pantalla_de_carga_sugiere_una_BO_y_Ctrl_Alt_B_la_usa()
    {
        if (Vision.Ocr.OcrReader.TryCreate() is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var host = new FakeHost();
        using var session = new CoachSessionViewModel(_data, host, () => null, () => new StillScreen("carga-ia-en-1080p.png"))
        {
            PlayerName = "Kyriokes",
        };
        session.Start(Scouts, new StillScreen("carga-ia-en-1080p.png"));
        for (var i = 0; i < 3; i++)
            await session.LiveCoach!.StepAsync();

        Assert.True(host.Lowered); // sin reloj, el overlay baja
        // El jugador juega con japoneses: desde D7 la biblioteca tiene una BO de cada civ (importada y mejorada) y gana
        // la mejorada de la suya.
        Assert.StartsWith("Sugerida: Fast Men-at-Arms (mejorada) (de tu civ", session.Overlay!.BoSuggestion);
        Assert.Contains("Ctrl+Alt+B", session.Overlay.BoSuggestion);
        // Segunda parte de D4: enfrente tiene al primero del otro equipo (cumanos).
        Assert.StartsWith("Contra cumanos (enfrente): ", session.Overlay.MatchAdvice);

        session.AcceptSuggestionCommand.Execute(null);
        Assert.Equal("Fast Men-at-Arms (mejorada)", session.LiveCoach!.BuildOrder.Name);
    }

    // D5: al detener el coach después de más de un minuto de partida, se guarda el informe y se muestra el resumen.
    [Fact]
    public async Task Al_detener_el_coach_guarda_el_informe_y_muestra_el_resumen()
    {
        var host = new FakeHost();
        var folder = Path.Combine(_library.FullName, "informes");
        using var session = new CoachSessionViewModel(_data, host, () => null, () => throw new InvalidOperationException()) { ReportsFolder = folder };
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
        while (session.LiveCoach!.Report().Length < 70 && await session.LiveCoach.StepAsync())
        {
        }

        session.Stop();

        var (summary, path) = host.Report!.Value;
        Assert.StartsWith(Scouts.Name, summary[0]);
        Assert.True(File.Exists(path));
        Assert.Equal(folder, Path.GetDirectoryName(path));
        Assert.Contains("<h2>Pasos contra la BO</h2>", File.ReadAllText(path));
    }

    [Fact]
    public void Sin_un_minuto_de_partida_no_hay_informe()
    {
        var host = new FakeHost();
        using var session = new CoachSessionViewModel(_data, host, () => null, () => throw new InvalidOperationException())
        {
            ReportsFolder = Path.Combine(_library.FullName, "informes"),
        };
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
        session.Stop();

        Assert.Null(host.Report);
    }

    [Fact]
    public void El_nombre_del_jugador_se_guarda_y_llega_al_coach()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path);
        Assert.Equal("", session.PlayerName);

        session.PlayerName = "Kyriokes ";
        session.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));

        Assert.Equal("Kyriokes ", session.LiveCoach!.PlayerName);
        Assert.Equal("Kyriokes", CoachSettings.Load(path).PlayerName);
        Assert.Equal("Kyriokes", new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException(), path).PlayerName);
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

    [Fact]
    public void No_arranca_con_una_BO_con_errores()
    {
        // Un .json importado a mano no pasa por el editor ni por la web: con un count enorme el Planner agotaba la memoria.
        var broken = Scouts with { Steps = Scouts.Steps.Select(s => s.What == "villager" ? s with { Count = 2_000_000_000 } : s).ToList() };
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => null, () => throw new InvalidOperationException());

        session.Start(broken, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));

        Assert.False(session.IsRunning);
        Assert.Contains("tiene errores", session.Status);
        Assert.Contains("count", session.Status);
    }

    // D6 (2026-10-21): el coach arranca solo cuando corre el juego, con la última BO usada, y se detiene cuando se cierra.
    [Fact]
    public void Arranca_solo_con_el_juego_y_se_detiene_cuando_se_cierra()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        var host = new FakeHost();
        using var session = new CoachSessionViewModel(_data, host, () => Scouts, () => new RecordedFrames(RecordedSession.Load(Fixture), speed: 50), path)
        {
            ReportsFolder = Path.Combine(_library.FullName, "informes"),
        };
        Assert.True(session.AutoStart);

        session.Tick(gameRunning: false);
        Assert.False(session.IsRunning);
        session.Tick(gameRunning: true);
        Assert.True(session.IsRunning);
        Assert.Equal(Scouts.Id, session.LastBuildOrderId);
        Assert.Equal(Scouts.Id, CoachSettings.Load(path).LastBuildOrder);

        session.Tick(gameRunning: false);
        Assert.False(session.IsRunning);
        Assert.Contains("se cerró", session.Status);
    }

    [Fact]
    public void Detenido_a_mano_no_vuelve_a_arrancar_hasta_que_el_juego_se_abra_de_nuevo()
    {
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => Scouts, () => new RecordedFrames(RecordedSession.Load(Fixture), speed: 50))
        {
            ReportsFolder = Path.Combine(_library.FullName, "informes"),
        };
        session.Tick(gameRunning: true);
        Assert.True(session.IsRunning);

        session.StartStopCommand.Execute(null);
        session.Tick(gameRunning: true);
        Assert.False(session.IsRunning);

        session.Tick(gameRunning: false);
        session.Tick(gameRunning: true);
        Assert.True(session.IsRunning);

        session.AutoStart = false;
        session.StartStopCommand.Execute(null);
        session.Tick(gameRunning: false);
        session.Tick(gameRunning: true);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Arrancado_a_mano_no_se_detiene_con_el_juego_y_el_coach_automatico_queda_en_los_ajustes()
    {
        var path = Path.Combine(_library.FullName, "settings.json");
        using var session = new CoachSessionViewModel(_data, new FakeHost(), () => Scouts, () => new RecordedFrames(RecordedSession.Load(Fixture), speed: 50), path)
        {
            ReportsFolder = Path.Combine(_library.FullName, "informes"),
        };
        session.StartStopCommand.Execute(null);
        session.Tick(gameRunning: false);
        Assert.True(session.IsRunning); // un replay no tiene juego

        session.AutoStart = false;
        Assert.False(CoachSettings.Load(path).AutoStart);
    }

    /// <summary>Capturas con el juego abierto pero atrás (alt-tab).</summary>
    private sealed class HiddenFrames : IFrameSource
    {
        private int _served;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(500);
        public PixelRect Area => new(0, 0, 1920, 1080);
        public Frame? Next() => new(_ => null, ++_served * 0.5, Area, InFront: false);
    }

    [Fact]
    public async Task Con_el_juego_atras_el_overlay_se_esconde()
    {
        var host = new FakeHost();
        using var session = new CoachSessionViewModel(_data, host, () => null, () => throw new InvalidOperationException());
        session.Start(Scouts, new HiddenFrames());

        await session.LiveCoach!.StepAsync();

        Assert.False(host.Visible);
        Assert.Equal("El juego no está al frente.", session.Overlay!.Warning);
    }
}
