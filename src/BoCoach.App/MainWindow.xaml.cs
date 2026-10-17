using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BoCoach.App.Calibration;
using BoCoach.App.Capture;
using BoCoach.App.Coaching;
using BoCoach.App.Editor;
using BoCoach.App.Overlay;
using BoCoach.App.Web;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;
using Microsoft.Win32;

namespace BoCoach.App;

public partial class MainWindow : Window, IDialogs, ICaptureHost, ICoachHost
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ScreenCapture _screen = new();
    private readonly RecordingIndicator _indicator = new();
    private GlobalHotkeys? _hotkeys;
    private CoachOverlay? _overlay;
    private ResourceOverlay? _resources;
    private bool _calibrating;

    public MainWindow(CoachData data)
    {
        InitializeComponent();
        ViewModel = new MainViewModel(data, this);
        DataContext = ViewModel;
        Tools = new CaptureToolsViewModel(_screen, this);
        ToolsBar.DataContext = Tools;
        Coach = new CoachSessionViewModel(data, this, () => ViewModel.SelectedEntry?.BuildOrder, () => new ScreenFrames(_screen, Tools.Layout), CoachSettings.DefaultPath);
        CoachButton.DataContext = Coach;
        MaxVillagersBox.DataContext = Coach;
        LivePanel.DataContext = Coach;
        Coach.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CoachSessionViewModel.Status))
                ViewModel.Status = Coach.Status;
        };

        _tick.Tick += (_, _) =>
        {
            Tools.Tick();
            if (Tools.IsRecording)
                _indicator.SetText($"● {Tools.RecordingStatus}");
        };
        _tick.Start();
        Tools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CaptureToolsViewModel.IsRecording))
                return;
            if (Tools.IsRecording)
                _indicator.ShowOver(_screen.CaptureArea, "● Grabando");
            else
                _indicator.Hide();
        };
        SourceInitialized += (_, _) => RegisterHotkeys();
        Closing += (_, _) => ViewModel.LeaveCurrent();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _hotkeys?.Dispose();
            Coach.Dispose();
            Tools.Dispose();
            _indicator.Close();
        };
    }

    public MainViewModel ViewModel { get; }
    public CaptureToolsViewModel Tools { get; }
    public CoachSessionViewModel Coach { get; }

    /// <summary>
    /// Atajos que andan con el foco en el juego (todos con Ctrl+Alt, que el juego no usa): calibrar, grabar y
    /// manejar el coach sin salir con Alt+Tab. No le mandan nada al juego.
    /// </summary>
    private void RegisterHotkeys()
    {
        _hotkeys = new GlobalHotkeys(this);
        var keys = new (Key Key, string Name, Action Action)[]
        {
            (Key.C, "C", () => { if (!_calibrating) Tools.CalibrateCommand.Execute(null); }),
            (Key.R, "R", () => Tools.ToggleRecordingCommand.Execute(null)),
            (Key.S, "S", () => Coach.StartStopCommand.Execute(null)),
            (Key.Right, "→", () => Coach.AdvanceCommand.Execute(null)),
            (Key.Left, "←", () => Coach.BackCommand.Execute(null)),
            (Key.Down, "↓", () => Coach.SkipCommand.Execute(null)),
            (Key.P, "P", () => Coach.TogglePauseCommand.Execute(null)),
            (Key.H, "H", () => Coach.ToggleOverlayCommand.Execute(null)),
            (Key.D, "D", () => Coach.ToggleDebugCommand.Execute(null)),
        };
        var missing = keys.Where(k => !_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Alt, k.Key, k.Action)).Select(k => $"Ctrl+Alt+{k.Name}").ToList();
        ViewModel.Status = missing.Count > 0
            ? $"Otro programa ya usa {string.Join(", ", missing)}; usá los botones de arriba."
            : "Atajos listos, también desde el juego: Ctrl+Alt+S empieza el coach, Ctrl+Alt+C calibra y Ctrl+Alt+R graba.";
    }

    // ---- Overlay del coach ----

    public void ShowOverlay(OverlayViewModel overlay, PixelRect area)
    {
        CloseOverlay();
        _overlay = new CoachOverlay(overlay);
        _overlay.Show();
        _overlay.PlaceOver(area);
        _resources = new ResourceOverlay(overlay.Resources, Tools.Layout);
        _resources.Show();
        _resources.PlaceOver(area);
    }

    public void PlaceOverlay(PixelRect area)
    {
        _overlay?.PlaceOver(area);
        _resources?.PlaceOver(area);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_overlay is null)
            return;
        if (visible)
        {
            _overlay.Show();
            _resources?.Show();
        }
        else
        {
            _overlay.Hide();
            _resources?.Hide();
        }
    }

    public void CloseOverlay()
    {
        _overlay?.Close();
        _overlay = null;
        _resources?.Close();
        _resources = null;
    }

    public void RunOnUi(Action action) => Dispatcher.BeginInvoke(action);

    // ---- Calibración y carpetas ----

    public RegionLayout? Calibrate(RegionLayout current)
    {
        // Si se abrió desde el editor, se minimiza para que debajo de la capa transparente quede el juego.
        // Si se abrió con el atajo desde el juego, el editor ya está atrás y no se toca.
        var fromEditor = IsActive;
        var previous = WindowState;
        if (fromEditor)
            WindowState = WindowState.Minimized;
        _calibrating = true;
        try
        {
            var window = new CalibrationWindow(current, _screen, OcrReader.TryCreate());
            return window.ShowDialog() == true ? window.Layout : null;
        }
        finally
        {
            _calibrating = false;
            if (fromEditor)
            {
                WindowState = previous;
                Activate();
            }
        }
    }

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    // ---- Reordenar pasos arrastrando ----

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement { DataContext: StepViewModel step })
        {
            ViewModel.Editor!.SelectedStep = step;
            DragDrop.DoDragDrop(StepList, step, DragDropEffects.Move);
        }
    }

    private void StepList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(StepViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void StepList_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel.Editor is not { } editor || e.Data.GetData(typeof(StepViewModel)) is not StepViewModel dragged)
            return;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as StepViewModel;
        var to = target is null ? editor.Steps.Count - 1 : editor.Steps.IndexOf(target);
        editor.MoveStep(editor.Steps.IndexOf(dragged), to);
        editor.SelectedStep = dragged;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    // ---- Diálogos ----

    public string? PickFileToOpen()
    {
        var dialog = new OpenFileDialog { Filter = "Build order (*.json)|*.json", Title = "Importar build order" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickFileToSave(string suggestedName)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var name = new string(suggestedName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        var dialog = new SaveFileDialog { Filter = "Build order (*.json)|*.json", FileName = name, Title = "Exportar build order" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public bool AskSaveChanges(string name) =>
        MessageBox.Show(this, $"\"{name}\" tiene cambios sin guardar. ¿Guardarlos?", "Cambios sin guardar",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool Confirm(string message) =>
        MessageBox.Show(this, message, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

    public string? AskText(string title, string message, string initial = "") => TextPrompt.Ask(this, title, message, initial);

    public WebSettings? EditWebAccount(WebSettings current)
    {
        var window = new WebAccountWindow(current) { Owner = this };
        return window.ShowDialog() == true ? window.Result : null;
    }

    /// <summary>Solo direcciones http(s): las arma la web, pero no se abre cualquier cosa.</summary>
    public void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
