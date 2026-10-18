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

        var args = e.Args;
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
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _ = window.ViewModel.ImportFromLinkAsync(importLink));
            _instance!.Listen(message => Dispatcher.BeginInvoke(async () =>
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
                if (message != ActivateMessage)
                    await window.ViewModel.ImportFromLinkAsync(message);
            }));
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
    /// Simula la franja de color del mod en cada recurso (filas 2 a 16 y columnas 1 a 36 de la caja, como en las
    /// grabaciones) y usa la calibración del usuario, para ver dónde cae la GD sin el juego abierto.
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
                target.Band = new Vision.Capture.PixelRect(1, 2, 36, 15);
        }
        var resources = new ResourceOverlay(overlay.Resources, layout);
        resources.Show();
        resources.PlaceOver(new Vision.Capture.PixelRect(-30000, 0, 1920, 1080));
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Render(window, path);
            Render(resources, Path.ChangeExtension(path, null) + "-recursos.png");
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
