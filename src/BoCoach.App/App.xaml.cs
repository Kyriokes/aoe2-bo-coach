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

        var args = Web.LinkProtocol.StartupArguments(e.Args);
        var screenshot = Argument(args, "--screenshot") ?? Argument(args, "--overlay-screenshot");
        var importLink = Argument(args, "--import");

        // Una sola app por usuario: si ya hay una abierta, se le pasa el enlace bocoach:// (o que se muestre) y esta cierra.
        if (screenshot is null)
        {
            _instance = SingleInstance.Claim(importLink ?? ActivateMessage);
            if (_instance is null)
            {
                Shutdown(0);
                return;
            }
        }

        CoachData data;
        try
        {
            // Para la captura se usa una biblioteca temporal, así no se toca la del usuario.
            data = CoachData.Load(screenshot is null ? null : Directory.CreateTempSubdirectory("bocoach-shot-").FullName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron cargar los datos del juego.\n\n{ex.Message}", "AoE2 BO Coach",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (Argument(args, "--overlay-screenshot") is { } overlayShot)
        {
            CaptureOverlayAndExit(data, overlayShot, Argument(args, "--readings"), Argument(args, "--bo"), Argument(args, "--at"), args.Contains("--debug"), args.Contains("--bands"));
            return;
        }

        var window = new MainWindow(data);
        MainWindow = window;
        if (screenshot is null)
        {
            window.Show();
            if (Argument(args, "--replay") is { } replay)
                StartReplay(window, replay, Argument(args, "--bo"), Argument(args, "--speed"), Argument(args, "--stats"));
            if (importLink is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = window.ViewModel.ImportFromLinkAsync(importLink, confirm: true));
            else if (Argument(args, "--replay") is null)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, window.ShowFirstRunIfNeeded);
            _instance!.Listen(message => Dispatcher.BeginInvoke(async () =>
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
                if (message != ActivateMessage)
                    await window.ViewModel.ImportFromLinkAsync(message, confirm: true);
            }));
            return;
        }

        if (args.Contains("--first-run"))
        {
            CaptureFirstRunAndExit(window, data, screenshot, Argument(args, "--loading"));
            return;
        }
        if (args.Contains("--calibration"))
        {
            CaptureCalibrationAndExit(screenshot, Argument(args, "--background"));
            return;
        }
        CaptureAndExit(window, data, screenshot, Argument(args, "--bo"), Argument(args, "--open"), Argument(args, "--select"));
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
        var window = new FirstRun.FirstRunWindow(tutorial, owner.Coach, owner.Tools.InstallModCommand) { ShowActivated = false, Left = -30000 };
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

    /// <summary>Una pantalla guardada en lugar de la del juego (para las capturas de prueba).</summary>
    private sealed class StillScreen(CapturedImage image) : IScreenSource
    {
        public PixelRect CaptureArea => new(0, 0, image.Width, image.Height);
        public bool GameFound => true;
        public CapturedImage Capture(PixelRect rect) => image.Crop(rect);
    }

    /// <summary>
    /// Modo de prueba sin el juego: corre el coach en vivo sobre una grabación, con el overlay en pantalla.
    /// Uso: BoCoach.exe --replay carpeta [--bo nombre] [--speed 4] [--stats uso.csv] (CPU y RAM cada 2 s).
    /// </summary>
    private static void StartReplay(MainWindow window, string folder, string? bo, string? speed, string? stats)
    {
        var vm = window.ViewModel;
        if (bo is not null)
            vm.SelectedEntry = vm.Entries.FirstOrDefault(entry => entry.Name.Contains(bo, StringComparison.OrdinalIgnoreCase)) ?? vm.SelectedEntry;
        if (vm.SelectedEntry?.BuildOrder is not { } buildOrder)
            return;

        var factor = double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1;
        window.Coach.Start(buildOrder, new RecordedFrames(RecordedSession.Load(folder), factor));
        if (stats is null)
            return;

        var log = new StreamWriter(stats) { AutoFlush = true };
        log.WriteLine("segundos,cpu_porcentaje,ram_mb,ms_por_captura");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        window.Coach.FrameApplied += frame =>
        {
            if (clock.Elapsed - last < TimeSpan.FromSeconds(2))
                return;
            last = clock.Elapsed;
            log.WriteLine(string.Join(",",
                ((int)clock.Elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                frame.Usage.CpuPercent.ToString("0.00", CultureInfo.InvariantCulture),
                frame.Usage.MemoryMb.ToString("0.0", CultureInfo.InvariantCulture),
                frame.FrameMs.ToString("0.0", CultureInfo.InvariantCulture)));
        };
        window.Closed += (_, _) => log.Dispose();
    }

    /// <summary>
    /// Dibuja el overlay como se vería en un momento de una partida grabada y cierra.
    /// Uso: BoCoach.exe --overlay-screenshot salida.png --readings lecturas.csv --bo nombre --at 8:20 [--debug].
    /// </summary>
    /// <param name="bands">
    /// Simula el casillero del diseño nuevo en cada recurso (el de la celda a 1080p) y la celda entera de inactivos, y
    /// usa la calibración del usuario, para ver dónde cae la GD y el marco de inactivos sin el juego abierto.
    /// </param>
    private void CaptureOverlayAndExit(CoachData data, string path, string? readings, string? bo, string? at, bool debug, bool bands = false)
    {
        var entry = data.Library.List().FirstOrDefault(e => bo is null || e.Name.Contains(bo, StringComparison.OrdinalIgnoreCase));
        if (entry?.BuildOrder is not { } buildOrder || readings is null)
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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// Modo de verificación: abre la ventana fuera de la pantalla, opcionalmente con una BO y un paso elegidos,
    /// la guarda como PNG y cierra.
    /// Uso: BoCoach.exe --screenshot salida.png [--bo nombre | --open archivo.json] [--select número].
    /// </summary>
    private void CaptureAndExit(MainWindow window, CoachData data, string path, string? bo, string? open, string? select)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();

        var vm = window.ViewModel;
        if (open is not null)
        {
            var imported = data.Library.Import(open);
            vm.Reload();
            vm.SelectedEntry = vm.Entries.FirstOrDefault(entry => entry.BuildOrder?.Id == imported.Id);
        }
        else if (bo is not null)
            vm.SelectedEntry = vm.Entries.FirstOrDefault(entry => entry.Name.Contains(bo, StringComparison.OrdinalIgnoreCase)) ?? vm.SelectedEntry;
        if (int.TryParse(select, out var n) && vm.Editor is { } editor && n >= 1 && n <= editor.Steps.Count)
            editor.SelectedStep = editor.Steps[n - 1];

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Render(window, path);
            Shutdown(0);
        });
    }

    /// <summary>Lo que manda una segunda instancia sin enlace: solo que se muestre la ventana.</summary>
    private const string ActivateMessage = "activate";

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static string? Argument(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Ocurrió un error inesperado.\n\n{e.Exception.Message}", "AoE2 BO Coach",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
