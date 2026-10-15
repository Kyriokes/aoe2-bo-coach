using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BoCoach.App.Calibration;
using BoCoach.App.Capture;
using BoCoach.App.Editor;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Regions;
using Microsoft.Win32;

namespace BoCoach.App;

public partial class MainWindow : Window, IDialogs, ICaptureHost
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(CoachData data)
    {
        InitializeComponent();
        ViewModel = new MainViewModel(data, this);
        DataContext = ViewModel;
        Tools = new CaptureToolsViewModel(new ScreenCapture(), this);
        ToolsBar.DataContext = Tools;

        _tick.Tick += (_, _) => Tools.Tick();
        _tick.Start();
        Closing += (_, _) => ViewModel.LeaveCurrent();
        Closed += (_, _) =>
        {
            _tick.Stop();
            Tools.Dispose();
        };
    }

    public MainViewModel ViewModel { get; }
    public CaptureToolsViewModel Tools { get; }

    // ---- Calibración y carpetas ----

    public RegionLayout? Calibrate(RegionLayout current)
    {
        // El editor se minimiza para que debajo de la capa transparente quede el juego, no esta ventana.
        var previous = WindowState;
        WindowState = WindowState.Minimized;
        try
        {
            var window = new CalibrationWindow(current, new ScreenCapture(), OcrReader.TryCreate());
            return window.ShowDialog() == true ? window.Layout : null;
        }
        finally
        {
            WindowState = previous;
            Activate();
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
}
