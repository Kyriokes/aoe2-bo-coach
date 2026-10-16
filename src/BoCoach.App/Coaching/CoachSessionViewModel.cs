using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Ocr;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Coaching;

/// <summary>Lo que la vista hace por la sesión: la ventana del overlay y el hilo de la interfaz.</summary>
public interface ICoachHost
{
    void ShowOverlay(OverlayViewModel overlay, PixelRect area);

    /// <summary>Vuelve a ubicar el overlay (la ventana del juego se movió o cambió de tamaño).</summary>
    void PlaceOverlay(PixelRect area);

    void SetOverlayVisible(bool visible);

    void CloseOverlay();

    void RunOnUi(Action action);
}

/// <summary>Empezar y detener el coach con la BO elegida, y los atajos que lo corrigen durante la partida.</summary>
public sealed partial class CoachSessionViewModel : ObservableObject, IDisposable
{
    private readonly CoachData _data;
    private readonly ICoachHost _host;
    private readonly Func<BuildOrder?> _selected;
    private readonly Func<IFrameSource> _liveFrames;
    private LiveCoach? _coach;
    private PixelRect _placedOver;
    private bool _overlayVisible = true;

    /// <param name="selected">La BO elegida en la biblioteca (la versión guardada).</param>
    /// <param name="liveFrames">Las capturas de la pantalla en vivo.</param>
    public CoachSessionViewModel(CoachData data, ICoachHost host, Func<BuildOrder?> selected, Func<IFrameSource> liveFrames)
    {
        _data = data;
        _host = host;
        _selected = selected;
        _liveFrames = liveFrames;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

    public string ButtonText => IsRunning ? "■ Detener coach" : "▶ Empezar coach";

    public OverlayViewModel? Overlay { get; private set; }

    /// <summary>Cada captura procesada (en el hilo de la interfaz), por ejemplo para medir CPU y RAM.</summary>
    public event Action<CoachFrame>? FrameApplied;

    [RelayCommand]
    private void StartStop()
    {
        if (IsRunning)
        {
            Stop();
            return;
        }
        if (_selected() is not { } bo)
        {
            Status = "Elegí una build order en la biblioteca para empezar el coach.";
            return;
        }
        Start(bo, _liveFrames());
    }

    public void Start(BuildOrder bo, IFrameSource frames)
    {
        Stop();
        Overlay = new OverlayViewModel(_data.GameData, bo.Name) { ShowDebug = Overlay?.ShowDebug ?? false };
        var coach = _coach = new LiveCoach(bo, _data.GameData, _data.Events, _data.Fonts, OcrReader.TryCreate(), frames);
        // Una captura que el loop termina después de detenerlo no tiene que llegar al overlay de otra sesión.
        coach.Updated += frame => _host.RunOnUi(() =>
        {
            if (_coach == coach)
                Apply(frame);
        });
        _placedOver = frames.Area;
        _overlayVisible = true;
        _host.ShowOverlay(Overlay, _placedOver);
        _coach.Start();
        IsRunning = true;
        Status = $"Coach con \"{bo.Name}\". Desde el juego: Ctrl+Alt+→ avanza, Ctrl+Alt+← retrocede, Ctrl+Alt+↓ saltea, Ctrl+Alt+H oculta.";
    }

    public void Stop()
    {
        if (_coach is null)
            return;
        _coach.Stop();
        _coach = null;
        _host.CloseOverlay();
        IsRunning = false;
        Status = "Coach detenido.";
    }

    [RelayCommand]
    private void Advance() => _coach?.Advance();

    [RelayCommand]
    private void Back() => _coach?.Back();

    [RelayCommand]
    private void Skip() => _coach?.Skip();

    [RelayCommand]
    private void TogglePause()
    {
        if (_coach is not null)
            _coach.Paused = !_coach.Paused;
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        if (_coach is null)
            return;
        _overlayVisible = !_overlayVisible;
        _host.SetOverlayVisible(_overlayVisible);
    }

    [RelayCommand]
    private void ToggleDebug()
    {
        if (Overlay is not null)
            Overlay.ShowDebug = !Overlay.ShowDebug;
    }

    public void Dispose() => Stop();

    private void Apply(CoachFrame frame)
    {
        if (Overlay is null)
            return;
        Overlay.Apply(frame);
        if (frame.Area is { IsEmpty: false } area && area != _placedOver)
        {
            _placedOver = area;
            _host.PlaceOverlay(area);
        }
        FrameApplied?.Invoke(frame);
    }
}
