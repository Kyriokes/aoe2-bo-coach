using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BoCoach.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var args = e.Args;
        var screenshot = Argument(args, "--screenshot");

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

        var window = new MainWindow(data);
        MainWindow = window;
        if (screenshot is null)
        {
            window.Show();
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
