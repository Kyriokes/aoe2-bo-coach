using System.IO;
using BoCoach.App.Overlay;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Economy;
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
    private readonly string? _settingsPath;

    /// <param name="selected">La BO elegida en la biblioteca (la versión guardada).</param>
    /// <param name="liveFrames">Las capturas de la pantalla en vivo.</param>
    /// <param name="settingsPath">Dónde se guardan los ajustes (máximo de aldeanos); null para no guardarlos (tests).</param>
    public CoachSessionViewModel(CoachData data, ICoachHost host, Func<BuildOrder?> selected, Func<IFrameSource> liveFrames, string? settingsPath = null)
    {
        var settings = settingsPath is null ? new CoachSettings() : CoachSettings.Load(settingsPath);
        MaxVillagers = settings.MaxVillagers;
        CursorReminders = settings.CursorReminders;
        GameCiv = data.GameData.IsKnownCiv(settings.GameCiv) ? settings.GameCiv : BoCiv;
        _settingsPath = settingsPath;
        _data = data;
        _host = host;
        _selected = selected;
        _liveFrames = liveFrames;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>Hasta cuántos aldeanos producen sin parar los centros urbanos (se aplica al empezar el coach).</summary>
    [ObservableProperty] public partial int MaxVillagers { get; set; }

    partial void OnMaxVillagersChanged(int value)
    {
        var clamped = CoachSettings.Clamp(value);
        if (clamped != value)
        {
            MaxVillagers = clamped;
            return;
        }
        SaveSettings();
    }

    /// <summary>
    /// Recordatorios junto al mouse: una casa cuando quedan 3 lugares de población y un centro urbano cuando está
    /// parado. Se prende y apaga desde la ventana o con Ctrl+Alt+M, y queda guardado.
    /// </summary>
    [ObservableProperty] public partial bool CursorReminders { get; set; }

    partial void OnCursorRemindersChanged(bool value)
    {
        if (Overlay is not null)
            Overlay.CursorReminders = value;
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleCursorReminders() => CursorReminders = !CursorReminders;

    /// <summary>Opción del desplegable para jugar con la civ que dice la BO.</summary>
    public const string BoCiv = "(la de la BO)";

    /// <summary>
    /// Civ con la que se juega esta partida (clave de game_data) o <see cref="BoCiv"/>. El coach usa sus
    /// bonificaciones (costos, población de las casas, recursos iniciales) en vez de las de la civ de la BO, que en las
    /// importadas es "any". Partida 20261017-163738: incas con casas de 10 de población contadas como dos.
    /// </summary>
    [ObservableProperty] public partial string GameCiv { get; set; } = BoCiv;

    partial void OnGameCivChanged(string value) => SaveSettings();

    public IReadOnlyList<string> CivOptions => [BoCiv, .. _data.GameData.Civs.Keys.Order()];

    /// <summary>La civ que va a usar el coach con esta BO.</summary>
    private string CivFor(BuildOrder bo) => GameCiv is { Length: > 0 } civ && civ != BoCiv ? civ : bo.Civ;

    /// <summary>El coach en vivo mientras corre (para los tests).</summary>
    public LiveCoach? LiveCoach => _coach;

    private void SaveSettings()
    {
        if (_settingsPath is null)
            return;
        try
        {
            new CoachSettings { MaxVillagers = MaxVillagers, CursorReminders = CursorReminders, GameCiv = GameCiv == BoCiv ? "" : GameCiv }.Save(_settingsPath);
        }
        catch (IOException)
        {
            // Si no se puede guardar, vale para esta sesión.
        }
    }

    public string ButtonText => IsRunning ? "■ Detener coach" : "▶ Empezar coach";

    public OverlayViewModel? Overlay { get; private set; }

    /// <summary>Lo que se leyó en la última captura, para el panel "Lo que ve el coach" de la ventana.</summary>
    public LiveReadingViewModel Live { get; } = new();

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
        // La civ de la partida manda sobre la de la BO: el Planner, la economía y la detección de clics usan sus costos.
        bo = bo with { Civ = CivFor(bo) };
        Overlay = new OverlayViewModel(_data.GameData, bo.Name) { ShowDebug = Overlay?.ShowDebug ?? false, CursorReminders = CursorReminders };
        var coach = _coach = new LiveCoach(bo, _data.GameData, _data.Events, _data.Fonts, OcrReader.TryCreate(), frames,
            new EconomySettings { MaxVillagers = MaxVillagers });
        // Una captura que el loop termina después de detenerlo no tiene que llegar al overlay de otra sesión.
        coach.Updated += frame => _host.RunOnUi(() =>
        {
            if (_coach == coach)
                Apply(frame);
        });
        Live.Clear();
        _placedOver = frames.Area;
        _overlayVisible = true;
        _host.ShowOverlay(Overlay, _placedOver);
        _coach.Start();
        IsRunning = true;
        Status = $"Coach con \"{bo.Name}\"{(GameCiv != BoCiv ? $" como {bo.Civ}" : "")}. Desde el juego: Ctrl+Alt+→ avanza, Ctrl+Alt+← retrocede, Ctrl+Alt+↓ saltea, Ctrl+Alt+H oculta, Ctrl+Alt+M recordatorios en el mouse.";
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
        Live.Apply(frame);
        if (frame.Area is { IsEmpty: false } area && area != _placedOver)
        {
            _placedOver = area;
            _host.PlaceOverlay(area);
        }
        FrameApplied?.Invoke(frame);
    }
}
