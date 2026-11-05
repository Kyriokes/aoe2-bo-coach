using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoCoach.App.Coaching;
using BoCoach.App.Overlay;
using BoCoach.App.Web;
using BoCoach.Core;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;

namespace BoCoach.App;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        try
        {
            Begin(e);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Begin(StartupEventArgs e)
    {
        Themes.DarkTitleBar.ApplyToAllWindows();

        var args = Web.LinkProtocol.StartupArguments(e.Args);
        var screenshot = Argument(args, "--screenshot") ?? Argument(args, "--overlay-screenshot");
        // --preview: la ventana de verdad con ajustes, cuenta y caché temporales, para probar menús y pestañas sin el juego.
        var preview = args.Contains("--preview");
        var importLink = Argument(args, "--import");

        // Publicar una carpeta de BO no abre ventanas: puede correr con la app abierta (ver BulkPublish).
        if (Argument(args, "--publish") is { } publish)
        {
            Shutdown(PublishFolder(publish, args.Contains("--dry-run")));
            return;
        }

        // Una sola app por usuario: si ya hay una abierta, se le pasa el enlace bocoach:// (o que se muestre) y esta cierra.
        if (screenshot is null && !preview)
        {
            _instance = args.Contains(RestartArgument) ? ClaimAfterRestart() : SingleInstance.Claim(importLink ?? ActivateMessage);
            if (_instance is null)
            {
                Shutdown(0);
                return;
            }
        }

        // El idioma, antes de la primera ventana: el XAML lee los textos al cargarse. Las capturas lo piden con --lang.
        Lang.SetDefault(screenshot is null && !preview ? CoachSettings.Load(CoachSettings.DefaultPath).EffectiveLanguage : Argument(args, "--lang"));

        CoachData data;
        string? shotDir;
        try
        {
            // Para la captura se usan ajustes, cuenta y caché temporales, así no se tocan los del usuario.
            shotDir = screenshot is null && !preview ? null : Directory.CreateTempSubdirectory("bocoach-shot-").FullName;
            data = CoachData.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Texts.DataLoadFailed(ex.Message), Texts.AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (Argument(args, "--overlay-screenshot") is { } overlayShot)
        {
            CaptureOverlayAndExit(data, overlayShot, Argument(args, "--readings"), Argument(args, "--bo"), Argument(args, "--at"), args.Contains("--debug"), args.Contains("--bands"),
                int.TryParse(Argument(args, "--rival-click"), out var rival) ? rival : null);
            return;
        }

        // Las capturas usan ajustes, cuenta y caché temporales (con --dev, el modo desarrollador prendido; con --cache
        // <carpeta>, esa caché de BO, para ver la lista sin la web).
        if (shotDir is not null && Argument(args, "--cache") is { } cacheSource && Directory.Exists(cacheSource))
            CopyFolder(cacheSource, Path.Combine(shotDir, "cache"));
        var window = new MainWindow(data, shotDir is null ? null : Path.Combine(shotDir, "settings.json"), shotDir);
        if (shotDir is not null && args.Contains("--dev"))
            DevMode.Instance.IsOn = true;
        if (shotDir is not null && Argument(args, "--state") is { } state)
            window.Home.Preview(PreviewState(state));
        MainWindow = window;
        if (preview)
        {
            // Sin arrancar con el juego abierto: acá se mira la ventana, no se juega.
            window.Coach.AutoStart = false;
            // Sin robarle el foco a lo que esté usando el jugador.
            if (args.Contains("--no-activate"))
                (window.ShowActivated, window.ShowInTaskbar) = (false, false);
            window.Show();
            return;
        }
        if (screenshot is null)
        {
            window.Show();
            RefreshLinks();
            if (Argument(args, "--replay") is { } replay)
                StartReplay(window, replay, Argument(args, "--bo"), Argument(args, "--speed"), Argument(args, "--stats"));
            if (importLink is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = window.BuildOrders.HandleLinkAsync(importLink));
            else if (Argument(args, "--replay") is null)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, window.ShowFirstRunIfNeeded);
            _instance!.Listen(message => Dispatcher.BeginInvoke(async () =>
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
                if (message != ActivateMessage)
                    await window.BuildOrders.HandleLinkAsync(message);
            }));
            return;
        }

        if (args.Contains("--first-run"))
        {
            CaptureFirstRunAndExit(window, data, screenshot, Argument(args, "--loading"));
            return;
        }
        if (args.Contains("--hotkeys"))
        {
            CaptureHotkeysAndExit(screenshot);
            return;
        }
        if (args.Contains("--calibration"))
        {
            CaptureCalibrationAndExit(screenshot, Argument(args, "--background"));
            return;
        }
        if (Argument(args, "--settings") is { } tab)
        {
            CaptureOwnedAndExit(window, new Coaching.SettingsWindow(window.Home, int.TryParse(tab, out var index) ? index : 0) { ShowActivated = false, Left = -30000 }, screenshot);
            return;
        }
        if (args.Contains("--report"))
        {
            CaptureOwnedAndExit(window, new Coaching.ReportWindow(SampleReport, () => { }, Texts.NotePrompt(1), _ => Task.FromResult(true)) { ShowActivated = false, Left = -30000 }, screenshot);
            return;
        }
        // Con un momento armado a mano, el mensaje de abajo es el de una cuenta conectada.
        CaptureAndExit(window, screenshot, Argument(args, "--state") is null or "sin-cuenta" ? null : Texts.HotkeysReady("Ctrl+Alt+S", "Ctrl+Alt+→"));
    }

    /// <summary>Los enlaces bocoach:// siguen a esta copia de la app, si ya estaban registrados. Sin permiso, quedan como estaban.</summary>
    private static void RefreshLinks()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        try
        {
            new LinkProtocol().RefreshIfRegistered(exe);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }
    }

    /// <summary>BoCoach.exe --publish &lt;carpeta o .json&gt; [--dry-run] (ver <see cref="BulkPublish"/>).</summary>
    private static int PublishFolder(string source, bool dryRun)
    {
        // Sin consola, Console.Out escribe con la página de códigos de Windows y rompe los acentos.
        using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            var data = CoachData.Load();
            using var http = WebAccount.NewHttpClient(TimeSpan.FromSeconds(30));
            // Task.Run: sin el contexto de WPF, esperar acá el resultado no traba las respuestas de la web.
            return Task.Run(() => BulkPublish.RunAsync(data, new WebAccount(http), source, dryRun, output)).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            output.WriteLine($"No se pudo publicar: {e.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Como <see cref="CaptureAndExit"/>, pero dibuja la ventana de calibración a 1920×1080, sobre fondo gris
    /// o sobre una captura del juego (<c>--background captura.png</c>, ubicada arriba a la izquierda, 1:1).
    /// </summary>
    private void CaptureCalibrationAndExit(string path, string? background)
    {
        Brush brush = background is null
            ? new SolidColorBrush(Color.FromRgb(0x50, 0x55, 0x48))
            : new ImageBrush(new BitmapImage(new Uri(Path.GetFullPath(background))))
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
        var window = new Calibration.CalibrationWindow(Vision.Regions.RegionLayout.Default, new Vision.Capture.ScreenCapture(), ocr: null)
        {
            Background = brush,
            ShowActivated = false,
            PlaceOverGame = false,
        };
        window.Width = 1920;
        window.Height = 1080;
        window.Left = -30000;
        window.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Render(window, path);
            window.Close();
            Shutdown(0);
        });
    }

    /// <summary>
    /// Dibuja el tutorial de primer uso (C4) y cierra. Con <c>--loading pantalla.png</c> lo muestra después de leer
    /// esa pantalla de carga, como si se la hubiera visto en el juego.
    /// Uso: BoCoach.exe --screenshot salida.png --first-run [--loading pantalla.png].
    /// </summary>
    private void CaptureFirstRunAndExit(MainWindow owner, CoachData data, string path, string? loading)
    {
        IScreenSource screen = loading is null ? new ScreenCapture() : new StillScreen(Png.Read(Path.GetFullPath(loading)));
        var tutorial = new FirstRun.FirstRunViewModel(screen, data.GameData, () => owner.Coach.Language, () => false, _ => { });
        var window = new FirstRun.FirstRunWindow(tutorial, owner.Coach, owner.Tools.InstallModCommand, owner.BuildOrders) { ShowActivated = false, Left = -30000 };
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            if (loading is not null)
            {
                tutorial.ToggleWatchingCommand.Execute(null);
                await tutorial.TickAsync();
            }
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Render(window, path);
            window.Close();
            Shutdown(0);
        });
    }

    /// <summary>
    /// Dibuja la ventana "Atajos" y cierra.
    /// Uso: BoCoach.exe --screenshot salida.png --hotkeys.
    /// </summary>
    private void CaptureHotkeysAndExit(string path)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            // Sin registrar nada en Windows: la captura puede correr con la app abierta.
            var map = new Hotkeys.HotkeyMap();
            var binder = new Hotkeys.HotkeyBinder(new NoHotkeys(), map, Hotkeys.HotkeyActions.All.ToDictionary(a => a.Id, _ => (Action)(() => { })), includeDeveloper: true);
            binder.RegisterAll();
            var window = new Hotkeys.HotkeysWindow(new Hotkeys.HotkeysViewModel(binder, map, includeDeveloper: true)) { ShowActivated = false, Left = -30000 };
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Render(window, path);
            window.Close();
            Shutdown(0);
        });
    }

    /// <summary>Dibuja una ventana secundaria (Ajustes, el informe) y cierra.</summary>
    private void CaptureOwnedAndExit(MainWindow owner, Window window, string path)
    {
        _ = owner;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Render(window, path);
            window.Close();
            Shutdown(0);
        });
    }

    /// <summary>Un informe de ejemplo para la captura.</summary>
    private static readonly string[] SampleReport =
    [
        "Aztec 5 Militia Rush: 25:30 de partida.",
        "Clic de Edad Feudal a las 10:31, objetivo 11:41 (la BO bien jugada: 11:48).",
        "Lo que más se atrasó: Construir galería de tiro (1:12 tarde); Campamento minero (1:05 tarde).",
        "Centro urbano parado: 1:07 en total; el hueco más largo, 0:31 desde las 11:23.",
        "Aldeanos inactivos: 0:12. Sin lugar por población: 0:24 (2 veces).",
        "Acumulaste madera: 1:10 con más de 500 desde las 18:02 (hasta 640).",
    ];

    /// <summary>Un momento del tablero armado a mano, para las capturas sin el juego.</summary>
    private static Coaching.HomeInputs PreviewState(string state)
    {
        var report = new Coaching.ReportInfo("informe.html", new DateTime(2026, 11, 2, 21, 19, 0));
        var finished = report with { Summary = "Edad Feudal 10:31 · 31 de 31 pasos · centro urbano parado 1:07", Length = 1530 };
        var connected = new Coaching.HomeInputs { Connected = true, AccountText = Texts.AccountConnectedAs("Kyriokes"), HasPlayerName = true, Report = report };
        return state switch
        {
            "sin-cuenta" => new Coaching.HomeInputs { AccountText = Texts.AccountNotConnected, HasPlayerName = false },
            "en-partida" => connected with
            {
                CoachRunning = true,
                GameOpen = true,
                InGame = true,
                GameTime = 492,
                Civ = "francos",
                Playing = "Aldeanos: 7 a 9 → madera   ·   Construir: casa a 13 de población",
            },
            "terminada" => connected with { Report = finished, ReportPending = true },
            "elegir" => connected with { AutoMode = false },
            _ => connected,
        };
    }

    private sealed class NoHotkeys : Hotkeys.IHotkeyRegistrar
    {
        private int _next;
        public int? Register(Hotkeys.HotkeyBinding binding, Action action) => ++_next;
        public void Unregister(int id) { }
    }

    /// <summary>Una pantalla guardada en lugar de la del juego (para las capturas de prueba).</summary>
    private sealed class StillScreen(CapturedImage image) : IScreenSource
    {
        public PixelRect CaptureArea => new(0, 0, image.Width, image.Height);
        public bool GameFound => true;
        public CapturedImage Capture(PixelRect rect) => image.Crop(rect);
    }

    /// <summary>
    /// Modo de prueba sin el juego: corre el coach en vivo sobre una grabación, con el overlay en pantalla.
    /// Uso: BoCoach.exe --replay carpeta [--bo ruta.json] [--speed 4] [--stats uso.csv] (CPU y RAM cada 2 s). Sin --bo,
    /// el coach corre sin BO.
    /// </summary>
    private static void StartReplay(MainWindow window, string folder, string? bo, string? speed, string? stats)
    {
        var buildOrder = bo is not null && File.Exists(bo) ? BuildOrderSerializer.Load(bo) : BuildOrder.None;

        var factor = double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1;
        window.Coach.Start(buildOrder, new RecordedFrames(RecordedSession.Load(folder), factor));
        if (stats is null)
            return;

        var log = new Coaching.UsageCsv(stats);
        window.Coach.FrameApplied += log.Add;
        window.Closed += (_, _) => log.Dispose();
    }

    /// <summary>
    /// Dibuja el overlay como se vería en un momento de una partida grabada y cierra.
    /// Uso: BoCoach.exe --overlay-screenshot salida.png --readings lecturas.csv --bo ruta.json --at 8:20 [--debug].
    /// --bo es la ruta de un .json (una BO corta que ya terminó muestra la tarjeta de la BO terminada).
    /// --rival-click N pone el aviso del clic de edad del rival N (1 azul ... 8 naranja) y guarda también la otra mitad
    /// del titileo en "-titila.png".
    /// </summary>
    /// <param name="bands">
    /// Simula el casillero del diseño nuevo en cada recurso (el de la celda a 1080p) y la celda entera de inactivos, y
    /// usa la calibración del usuario, para ver dónde cae la GD y el marco de inactivos sin el juego abierto.
    /// </param>
    private void CaptureOverlayAndExit(CoachData data, string path, string? readings, string? bo, string? at, bool debug, bool bands = false,
        int? rivalClick = null)
    {
        var buildOrder = bo is not null && File.Exists(bo) ? BuildOrderSerializer.Load(bo) : null;
        if (buildOrder is null || readings is null)
        {
            Shutdown(2);
            return;
        }

        var until = GameClock.TryParse(at, out var seconds) ? seconds : double.MaxValue;
        var builder = new SnapshotBuilder(data.GameData);
        var coach = new Coach(buildOrder, data.GameData, data.Events);
        foreach (var logged in ScreenReadingLog.Load(readings))
        {
            if (builder.Build(logged.Reading) is not { } snapshot)
                continue;
            coach.Update(snapshot);
            if (snapshot.Time >= until)
                break;
        }

        var overlay = new OverlayViewModel(data.GameData, buildOrder.Name) { ShowDebug = debug };
        overlay.Apply(new CoachFrame(coach.View, null, 0, 31, new ProcessUsage(1.2, 87), false, false, null, GameWindow.PrimaryScreen));
        if (rivalClick is { } slot)
            (overlay.Alert, overlay.RivalSlot) = ("El rival hizo clic en Castillos (18:20): llega a las 21:00", slot);
        var window = new CoachOverlay(overlay) { Left = -30000, Top = 0 };
        window.Show();
        // La segunda parte: los números debajo de los recursos, en otra imagen ("-recursos.png").
        var layout = bands ? Vision.Regions.RegionLayout.LoadOrDefault(Vision.Regions.RegionLayout.DefaultPath) : Vision.Regions.RegionLayout.Default;
        if (bands)
        {
            foreach (var target in overlay.Resources)
            {
                target.Band = new Vision.Capture.PixelRect(45, 5, 30, 36);
                target.Chip = true;
            }
            overlay.IdleAlert.Band = new Vision.Capture.PixelRect(0, 0, 81, 46);
            overlay.IdleAlert.Chip = true;
            overlay.IdleAlert.Alert = true;
        }
        var resources = new ResourceOverlay(overlay.Resources, layout, overlay.IdleAlert);
        resources.Show();
        resources.PlaceOver(new Vision.Capture.PixelRect(-30000, 0, 1920, 1080));
        // Los recordatorios del mouse, sin mostrar la ventana (aparecería donde está el mouse): solo su contenido.
        overlay.HouseReminder = overlay.TownCenterReminder = overlay.IdleReminder = bands;
        overlay.CursorReminders = false; // así la ventana no se muestra
        var cursor = new CursorReminder(overlay);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Render(window, path);
            if (rivalClick is not null)
            {
                overlay.Blink();
                window.UpdateLayout();
                Render(window, Path.ChangeExtension(path, null) + "-titila.png");
            }
            Render(resources, Path.ChangeExtension(path, null) + "-recursos.png");
            if (bands && cursor.Content is FrameworkElement badges)
            {
                badges.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                badges.Arrange(new Rect(badges.DesiredSize));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(badges.ActualWidth), (int)Math.Ceiling(badges.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(badges);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.ChangeExtension(path, null) + "-mouse.png");
                encoder.Save(file);
            }
            cursor.Close();
            window.Close();
            resources.Close();
            Shutdown(0);
        });
    }

    private static void Render(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        // Solo lo de adentro: el marco de Windows no se dibuja y quedaba blanco en la imagen.
        BitmapSource image = bitmap;
        if (window.Content is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } content)
            image = new CroppedBitmap(bitmap, new Int32Rect(0, 0,
                Math.Min(bitmap.PixelWidth, (int)(content.ActualWidth + content.Margin.Left + content.Margin.Right)),
                Math.Min(bitmap.PixelHeight, (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom))));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// Modo de verificación: abre la ventana fuera de la pantalla, la guarda como PNG y cierra.
    /// Uso: BoCoach.exe --screenshot salida.png [--cache carpeta] [--dev].
    /// </summary>
    private void CaptureAndExit(MainWindow window, string path, string? status)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (status is not null)
                window.BuildOrders.Status = status;
            Render(window, path);
            Shutdown(0);
        });
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Lo que manda una segunda instancia sin enlace: solo que se muestre la ventana.</summary>
    private const string ActivateMessage = "activate";

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>La app se volvió a abrir sola (cambio de idioma): la de antes está terminando de cerrarse, y se la espera.</summary>
    internal const string RestartArgument = "--restart";

    private static SingleInstance? ClaimAfterRestart()
    {
        for (var tries = 0; tries < 50; tries++)
        {
            if (SingleInstance.Claim(null) is { } instance)
                return instance;
            Thread.Sleep(200);
        }
        return null;
    }

    private static string? Argument(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowError(e.Exception);
    }

    private bool _showingError;

    /// <summary>
    /// El cartel de un error inesperado, uno por vez: mientras está abierto el despachador sigue andando, y un error que se
    /// repite no abre otro encima. Sin una ventana a la vista la app se cierra: seguía abierta sin ventana y con la
    /// instancia única tomada, así que cada apertura siguiente esperaba 10 s y se cerraba sin decir nada.
    /// </summary>
    private void ShowError(Exception error)
    {
        if (_showingError)
            return;
        _showingError = true;
        try
        {
            MessageBox.Show(Texts.UnexpectedError(error.Message), Texts.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _showingError = false;
        }
        if (MainWindow is not { IsLoaded: true })
            Shutdown(1);
    }
}
