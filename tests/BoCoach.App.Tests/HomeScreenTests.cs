using System.Net.Http;
using BoCoach.App.Capture;
using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Core.Web;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Tests;

/// <summary>El tablero de la ventana principal (spec 2026-11-02): qué dice y qué ofrece en cada momento.</summary>
public sealed class HomeScreenTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-home-");
    private readonly CoachData _data = CoachData.Load();

    public void Dispose() => _dir.Delete(recursive: true);

    private static readonly HomeInputs Connected = new() { Connected = true, AccountText = "Conectada como Ana" };

    private static readonly ReportInfo Report = new("informe.html", new DateTime(2026, 11, 2, 21, 19, 0), "31 de 31 pasos", 1530);

    [Fact]
    public void Sin_cuenta_pide_conectarla_y_deja_empezar_igual()
    {
        var screen = HomeScreen.Build(new HomeInputs { AccountText = "x" });

        Assert.Equal(HomeState.NoAccount, screen.State);
        Assert.Equal(Texts.NoAccountTitle, screen.Title);
        Assert.Equal(HomeAction.Connect, screen.Primary?.Action);
        Assert.Equal(HomeAction.StartStop, screen.Secondary?.Action);
        Assert.Equal(HomeStepKind.Warning, screen.Account.Kind);
        Assert.Equal(HomeAction.Connect, screen.Account.Link?.Action);
    }

    [Fact]
    public void Con_el_juego_cerrado_espera_el_juego_y_la_accion_es_empezar()
    {
        var screen = HomeScreen.Build(Connected);

        Assert.Equal(HomeState.Waiting, screen.State);
        Assert.Equal(Texts.WaitingGameTitle, screen.Title);
        Assert.Equal(new HomeButton(Texts.StartNow, HomeAction.StartStop), screen.Primary);
        Assert.Null(screen.Secondary);
        Assert.Equal(HomeStepKind.Done, screen.Account.Kind);
        Assert.Equal(HomeStepKind.Done, screen.BuildOrder.Kind);
        Assert.Equal(HomeStepKind.Current, screen.Play.Kind);
    }

    [Fact]
    public void Con_el_coach_andando_espera_la_partida_y_solo_ofrece_detenerlo()
    {
        var closed = HomeScreen.Build(Connected with { CoachRunning = true });
        var open = HomeScreen.Build(Connected with { CoachRunning = true, GameOpen = true });

        Assert.Equal(Texts.WaitingMatchTitle, closed.Title);
        Assert.Equal(Texts.WaitingMatchGameClosed, closed.Detail);
        Assert.Equal(Texts.WaitingMatchGameOpen, open.Detail);
        Assert.Null(open.Primary);
        Assert.Equal(new HomeButton(Texts.StopCoach, HomeAction.StartStop), open.Secondary);
    }

    [Fact]
    public void Detenido_a_mano_con_el_juego_abierto_lo_dice()
    {
        var screen = HomeScreen.Build(Connected with { GameOpen = true });

        Assert.Equal(Texts.StoppedTitle, screen.Title);
        Assert.Equal(Texts.StoppedByYou, screen.Detail);
        Assert.Equal(HomeAction.StartStop, screen.Primary?.Action);
    }

    [Fact]
    public void Sin_el_coach_automatico_dice_que_esta_apagado()
    {
        var screen = HomeScreen.Build(Connected with { AutoStart = false });

        Assert.Equal(Texts.StoppedTitle, screen.Title);
        Assert.Equal(Texts.StoppedAutoOff, screen.Detail);
    }

    [Fact]
    public void A_mano_sin_BO_pide_elegir_una()
    {
        var screen = HomeScreen.Build(Connected with { AutoMode = false });

        Assert.Equal(Texts.NeedsBuildOrder, screen.Detail);
        Assert.Equal(HomeStepKind.Warning, screen.BuildOrder.Kind);
        Assert.Equal(HomeStepKind.Todo, screen.Play.Kind);

        var chosen = HomeScreen.Build(Connected with { AutoMode = false, SelectedName = "22p Scouts" });
        Assert.Equal(Texts.WaitingGameDetail, chosen.Detail);
        Assert.Equal(Texts.StepBuildOrderManual("22p Scouts"), chosen.BuildOrder.Detail);
        Assert.Equal(HomeStepKind.Done, chosen.BuildOrder.Kind);
    }

    [Fact]
    public void En_partida_muestra_el_reloj_la_civ_y_lo_que_pide_el_coach()
    {
        var screen = HomeScreen.Build(Connected with { CoachRunning = true, InGame = true, GameTime = 492, Civ = "francos", Playing = "Aldeanos: a madera" });

        Assert.Equal(HomeState.InGame, screen.State);
        Assert.Equal("En partida · 8:12 · francos", screen.Title);
        Assert.Equal("Aldeanos: a madera", screen.Detail);
        Assert.Equal(HomeAction.Advance, screen.Primary?.Action);
        Assert.Equal(new HomeButton(Texts.StopCoach, HomeAction.StartStop), screen.Secondary);
        Assert.Equal(HomeStepKind.Done, screen.Play.Kind);
    }

    [Fact]
    public void En_partida_gana_aunque_no_haya_cuenta_ni_civ()
    {
        var screen = HomeScreen.Build(new HomeInputs { CoachRunning = true, InGame = true, GameTime = 60 });

        Assert.Equal(HomeState.InGame, screen.State);
        Assert.Equal("En partida · 1:00", screen.Title);
        Assert.Equal(Texts.InGameFallback, screen.Detail);
    }

    [Fact]
    public void Terminada_ofrece_el_informe_hasta_que_se_lo_da_por_visto()
    {
        var screen = HomeScreen.Build(Connected with { Report = Report, ReportPending = true });

        Assert.Equal(HomeState.Finished, screen.State);
        Assert.Equal("Partida terminada · 25:30", screen.Title);
        Assert.Equal("31 de 31 pasos", screen.Detail);
        Assert.Equal(HomeAction.OpenReport, screen.Primary?.Action);
        Assert.Equal(HomeAction.ReportSeen, screen.Secondary?.Action);

        Assert.Equal(HomeState.Waiting, HomeScreen.Build(Connected with { Report = Report }).State);
    }

    [Fact]
    public void Un_informe_de_antes_de_abrir_la_app_solo_tiene_la_fecha()
    {
        var old = new ReportInfo("informe.html", new DateTime(2026, 11, 2, 21, 19, 0));

        var screen = HomeScreen.Build(Connected with { Report = old });
        Assert.Equal("Último: 2/11 21:19", screen.Report.Detail);
        Assert.Equal(HomeStepKind.Info, screen.Report.Kind);
        Assert.Equal(HomeAction.OpenReport, screen.Report.Link?.Action);
        Assert.Equal(HomeAction.OpenReports, screen.Report.SecondLink?.Action);

        Assert.Equal("Último: 2/11 21:19 · 25:30 de partida", HomeScreen.Build(Connected with { Report = Report }).Report.Detail);
        Assert.Null(HomeScreen.Build(Connected).Report.Link);
    }

    [Fact]
    public void Sin_tu_nombre_lleva_a_primeros_pasos()
    {
        var screen = HomeScreen.Build(Connected with { HasPlayerName = false });

        Assert.Equal(Texts.StepPlayNoName, screen.Play.Detail);
        Assert.Equal(HomeAction.FirstRun, screen.Play.Link?.Action);
        Assert.Null(HomeScreen.Build(Connected).Play.Link);
    }

    // ---- El último informe y lo que el coach le cuenta al tablero ----

    [Fact]
    public void El_ultimo_informe_sale_del_nombre_del_archivo_y_no_mira_las_carpetas_de_adentro()
    {
        var folder = _dir.CreateSubdirectory("informes");
        File.WriteAllText(Path.Combine(folder.FullName, "20261101-100000.html"), "");
        File.WriteAllText(Path.Combine(folder.FullName, "20261102-211900.html"), "");
        File.WriteAllText(Path.Combine(folder.FullName, "otra-cosa.html"), "");
        File.WriteAllText(Path.Combine(folder.CreateSubdirectory("de-los-tests").FullName, "20261231-235959.html"), "");

        var report = CoachSessionViewModel.LatestReportIn(folder.FullName);

        Assert.Equal(new DateTime(2026, 11, 2, 21, 19, 0), report?.Date);
        Assert.EndsWith("20261102-211900.html", report?.Path);
        Assert.Null(CoachSessionViewModel.LatestReportIn(Path.Combine(_dir.FullName, "no-existe")));
    }

    [Fact]
    public void El_renglon_del_informe_junta_clics_pasos_y_centro_urbano()
    {
        var report = new GameReport("BO", 1530,
            [new StepTiming(1, "a", 10, 12), new StepTiming(2, "b", 20, null)],
            [new AgeClick("Edad Feudal", 631, 761, null, 600), new AgeClick("Edad de los Castillos", null, null, null, null)],
            67, [], 0, 0, 0, []);

        Assert.Equal("Edad Feudal 10:31 · 1 de 2 pasos · centro urbano parado 1:07", CoachSessionViewModel.ReportLine(report));
    }

    private sealed class FakeCoachHost : ICoachHost
    {
        public void ShowOverlay(OverlayViewModel overlay, PixelRect area) { }
        public void PlaceOverlay(PixelRect area) { }
        public void SetOverlayVisible(bool visible) { }
        public void CloseOverlay() { }
        public void RunOnUi(Action action) => action();
        public void ShowReport(IReadOnlyList<string> summary, string path) { }
        public EloCandidate? ChooseElo(IReadOnlyList<EloCandidate> candidates) => null;
    }

    private sealed class FakeDialogs : IBuildOrderDialogs
    {
        public List<string> Opened { get; } = [];
        public bool Confirm(string message) => true;
        public string? AskText(string title, string message, string initial = "") => null;
        public WebSettings? EditWebAccount(WebSettings current) => null;
        public void OpenUrl(string url) => Opened.Add(url);
    }

    private sealed class FakeCapture : ICaptureHost, IHomeHost
    {
        public List<string> Files { get; } = [];
        public List<string> Folders { get; } = [];
        public int FirstRuns { get; private set; }
        public RegionLayout? Calibrate(RegionLayout current) => null;
        public void OpenFolder(string path) => Folders.Add(path);
        public void ShowMessage(string title, string text, bool warning) { }
        public void OpenFile(string path) => Files.Add(path);
        public void ShowFirstRun() => FirstRuns++;
    }

    private sealed class FakeScreen : IScreenSource
    {
        public PixelRect CaptureArea => new(0, 0, 1920, 1080);
        public bool GameFound => true;
        public CapturedImage Capture(PixelRect rect) => new(rect.Width, rect.Height, new byte[rect.Width * rect.Height * 4]);
    }

    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-214512");

    private string ReportsFolder => Path.Combine(_dir.FullName, "informes");

    private (HomeViewModel Home, CoachSessionViewModel Coach, FakeCapture Host, BuildOrdersViewModel BuildOrders, FakeDialogs Dialogs) Build()
    {
        var web = new WebAccount(new HttpClient(), Path.Combine(_dir.FullName, "web.json"));
        var cache = new BuildOrderCache(Path.Combine(_dir.FullName, "cache"), () => web.Client, () => web.Settings.Token);
        var dialogs = new FakeDialogs();
        var buildOrders = new BuildOrdersViewModel(cache, web, dialogs, _data.GameData);
        var host = new FakeCapture();
        var tools = new CaptureToolsViewModel(new FakeScreen(), host, Path.Combine(_dir.FullName, "regions.json"), Path.Combine(_dir.FullName, "grabaciones"), () => null, Path.Combine(_dir.FullName, "perfiles"));
        var coach = new CoachSessionViewModel(_data, new FakeCoachHost(), () => null, () => new RecordedFrames(RecordedSession.Load(Fixture), speed: 50)) { ReportsFolder = ReportsFolder };
        return (new HomeViewModel(coach, buildOrders, tools, host), coach, host, buildOrders, dialogs);
    }

    private CoachFrame Frame(bool inGame, double time)
    {
        var view = new Coach(Scouts, _data.GameData, _data.Events).Update(new GameSnapshot(time, PlanStart.Standard.Stock, 4, 5));
        return new CoachFrame(view, null, 0, 30, new ProcessUsage(1, 50), false, false, null, new PixelRect(0, 0, 1920, 1080), Civ: "franks", InGame: inGame);
    }

    [Fact]
    public void El_tablero_sigue_al_coach_de_verdad()
    {
        var (home, coach, _, _, _) = Build();
        using (coach)
        {
            Assert.Equal(HomeState.NoAccount, home.Screen.State);
            Assert.False(home.ShowLive);

            coach.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
            Assert.True(home.ShowLive);
            Assert.Equal(new HomeButton(Texts.StopCoach, HomeAction.StartStop), home.Screen.Secondary);

            coach.Apply(Frame(inGame: true, time: 492));
            Assert.Equal(HomeState.InGame, home.Screen.State);
            Assert.Equal("En partida · 8:12 · francos", home.Screen.Title);
            Assert.NotEqual(Texts.InGameFallback, home.Screen.Detail); // las líneas del coach

            coach.Stop();
            Assert.False(coach.InGame);
            Assert.False(home.ShowLive);
            Assert.Equal(HomeState.NoAccount, home.Screen.State);
        }
    }

    [Fact]
    public async Task Al_terminar_una_partida_ofrece_el_informe_y_otra_partida_lo_saca()
    {
        var (home, coach, host, _, _) = Build();
        using (coach)
        {
            Assert.Null(coach.LastReport);
            coach.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
            while (coach.LiveCoach!.Report().Length < 70 && await coach.LiveCoach.StepAsync())
            {
            }
            coach.Stop();

            Assert.Equal(HomeState.Finished, home.Screen.State);
            Assert.NotNull(coach.LastReport?.Summary);
            home.RunCommand.Execute(HomeAction.OpenReport);
            Assert.Equal(coach.LastReport!.Path, Assert.Single(host.Files));

            home.RunCommand.Execute(HomeAction.ReportSeen);
            Assert.NotEqual(HomeState.Finished, home.Screen.State);
            Assert.NotNull(coach.LastReport); // la tarjeta lo sigue mostrando

            coach.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
            while (coach.LiveCoach!.Report().Length < 70 && await coach.LiveCoach.StepAsync())
            {
            }
            coach.Stop();
            Assert.Equal(HomeState.Finished, home.Screen.State);

            coach.Start(Scouts, new RecordedFrames(RecordedSession.Load(Fixture), speed: 50));
            coach.Apply(Frame(inGame: true, time: 10));
            Assert.False(coach.ReportPending);
            Assert.Equal(HomeState.InGame, home.Screen.State);
        }
    }

    [Fact]
    public void Los_enlaces_hacen_lo_que_dicen()
    {
        var (home, coach, host, _, dialogs) = Build();
        using (coach)
        {
            home.RunCommand.Execute(HomeAction.OpenReports);
            Assert.Equal(ReportsFolder, Assert.Single(host.Folders));
            Assert.True(Directory.Exists(ReportsFolder));

            home.RunCommand.Execute(HomeAction.FirstRun);
            Assert.Equal(1, host.FirstRuns);

            home.RunCommand.Execute(HomeAction.Connect);
            Assert.EndsWith("/cuenta", Assert.Single(dialogs.Opened));

            home.RunCommand.Execute(HomeAction.StartStop);
            Assert.True(coach.IsRunning);
            home.RunCommand.Execute(HomeAction.StartStop);
            Assert.False(coach.IsRunning);
        }
    }

    [Fact]
    public void Abrir_el_juego_cambia_lo_que_dice_el_tablero()
    {
        var (home, coach, _, _, _) = Build();
        using (coach)
        {
            coach.PlayerName = "Ana";
            Assert.Equal(Texts.StepPlayGameClosed, home.Screen.Play.Detail);

            home.GameOpen = true;
            Assert.Equal(Texts.StepPlayGameOpen, home.Screen.Play.Detail);
        }
    }

    [Fact]
    public void Un_momento_armado_a_mano_gana_sobre_lo_de_verdad()
    {
        var (home, coach, _, _, _) = Build();
        using (coach)
        {
            home.Preview(Connected);
            Assert.Equal(Texts.WaitingGameTitle, home.Screen.Title);
            home.GameOpen = true;
            Assert.Equal(Texts.WaitingGameTitle, home.Screen.Title);
        }
    }

    [Fact]
    public void Una_lista_vacia_explica_que_hacer_segun_la_cuenta()
    {
        var (_, coach, _, buildOrders, _) = Build();
        using (coach)
        {
            Assert.False(buildOrders.HasEntries);
            Assert.Equal(Texts.ListEmptyNoAccount, buildOrders.EmptyListText);
        }
    }

    [Fact]
    public void El_editor_ya_no_aparece_en_el_titulo_de_la_ventana()
    {
        Assert.DoesNotContain("Editor", AppVersion.WindowTitle);
        Assert.StartsWith(Texts.AppName, AppVersion.WindowTitle);
    }
}
