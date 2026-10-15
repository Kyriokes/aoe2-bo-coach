using System.IO;
using BoCoach.Core;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Capture;

/// <summary>Lo que la vista hace por las herramientas de captura: abrir la calibración y carpetas.</summary>
public interface ICaptureHost
{
    /// <summary>Muestra la calibración y devuelve las regiones nuevas, o null si se canceló.</summary>
    RegionLayout? Calibrate(RegionLayout current);

    void OpenFolder(string path);
}

/// <summary>Calibrar la pantalla y grabar partidas (RecordSession).</summary>
public sealed partial class CaptureToolsViewModel : ObservableObject, IDisposable
{
    private readonly IScreenSource _screen;
    private readonly ICaptureHost _host;
    private readonly string _regionsPath;
    private readonly string _recordingsRoot;
    private SessionRecorder? _recorder;

    public CaptureToolsViewModel(IScreenSource screen, ICaptureHost host, string? regionsPath = null, string? recordingsRoot = null)
    {
        _screen = screen;
        _host = host;
        _regionsPath = regionsPath ?? RegionLayout.DefaultPath;
        _recordingsRoot = recordingsRoot ?? SessionRecorder.DefaultOutputRoot;
        UpdateCalibrationStatus();
    }

    [ObservableProperty] public partial string CalibrationStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty] public partial string RecordingStatus { get; set; } = "";

    public string RecordButtonText => IsRecording ? "■ Detener grabación" : "● Grabar partida";

    public bool IsCalibrated => File.Exists(_regionsPath);

    public RegionLayout Layout => RegionLayout.LoadOrDefault(_regionsPath);

    [RelayCommand]
    private void Calibrate()
    {
        if (_host.Calibrate(Layout) is not { } result)
            return;
        result.Save(_regionsPath);
        UpdateCalibrationStatus();
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (_recorder is { IsRecording: true } recorder)
        {
            recorder.Stop();
            IsRecording = false;
            RecordingStatus = $"Grabación guardada: {recorder.Frames} capturas en {recorder.Folder}";
            _recorder = null;
            return;
        }

        _recorder = new SessionRecorder(_screen, Layout, _recordingsRoot);
        _recorder.Start();
        IsRecording = true;
        Tick();
    }

    [RelayCommand]
    private void OpenRecordings()
    {
        Directory.CreateDirectory(_recordingsRoot);
        _host.OpenFolder(_recorder?.Folder ?? _recordingsRoot);
    }

    /// <summary>La vista lo llama cada segundo para actualizar el estado de la grabación.</summary>
    public void Tick()
    {
        if (_recorder is not { IsRecording: true } recorder)
            return;
        RecordingStatus = $"Grabando {GameClock.Format(recorder.Elapsed.TotalSeconds)} · {recorder.Frames} capturas"
            + (recorder.LastError is { } error ? $" · último error: {error}" : "");
    }

    public void Dispose() => _recorder?.Stop();

    private void UpdateCalibrationStatus()
    {
        if (!IsCalibrated)
        {
            CalibrationStatus = "Pantalla sin calibrar: se usan posiciones aproximadas.";
            return;
        }
        var layout = Layout;
        CalibrationStatus = $"Calibrada para {layout.ReferenceWidth}×{layout.ReferenceHeight} ({File.GetLastWriteTime(_regionsPath):d/M HH:mm}).";
    }
}
