using System.IO;
using System.Text.Json;
using BoCoach.App.Overlay;
using BoCoach.Core;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
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

    /// <summary>Un mensaje con aceptar (por ejemplo, cómo quedó la instalación del mod).</summary>
    void ShowMessage(string title, string text, bool warning);
}

/// <summary>Calibrar la pantalla, grabar partidas (RecordSession) e instalar el mod del HUD.</summary>
public sealed partial class CaptureToolsViewModel : ObservableObject, IDisposable
{
    private readonly IScreenSource _screen;
    private readonly ICaptureHost _host;
    private readonly string _regionsPath;
    private readonly string _recordingsRoot;
    private readonly Func<string?> _findGame;
    private readonly string _gameProfiles;
    private readonly Func<HudFonts> _fonts;
    private readonly Func<GameLanguage> _language;
    private readonly string _diagnosticsFolder;
    private SessionRecorder? _recorder;

    public CaptureToolsViewModel(IScreenSource screen, ICaptureHost host, string? regionsPath = null, string? recordingsRoot = null,
        Func<string?>? findGame = null, string? gameProfiles = null,
        Func<HudFonts>? fonts = null, Func<GameLanguage>? language = null, string? diagnosticsFolder = null)
    {
        _screen = screen;
        _host = host;
        _regionsPath = regionsPath ?? RegionLayout.DefaultPath;
        _recordingsRoot = recordingsRoot ?? SessionRecorder.DefaultOutputRoot;
        _findGame = findGame ?? GameIcons.FindGame;
        _gameProfiles = gameProfiles ?? HudMod.DefaultProfilesRoot;
        _fonts = fonts ?? (() => HudFonts.Load(Path.Combine(CoachData.DataDir, "fonts")));
        _language = language ?? (() => GameLanguage.English);
        _diagnosticsFolder = diagnosticsFolder ?? DiagnosticCapture.DefaultFolder;
        UpdateCalibrationStatus();
    }

    [ObservableProperty] public partial string CalibrationStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty] public partial string RecordingStatus { get; set; } = "";

    /// <summary>Si se encontró la ventana del juego y de qué tamaño (se actualiza en <see cref="Tick"/>).</summary>
    [ObservableProperty] public partial string GameStatus { get; set; } = "";

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

    /// <summary>
    /// Arma el mod "BoCoach HUD" en los perfiles del juego (cuadrados negros con los aldeanos de cada recurso y el
    /// reloj grande, que es lo que mejor lee el coach) y cuenta cómo quedó.
    /// </summary>
    [RelayCommand]
    private void InstallMod()
    {
        const string title = "Mod del HUD";
        if (_findGame() is not { } game)
        {
            _host.ShowMessage(title, "No encontré Age of Empires II: DE en las bibliotecas de Steam. Abrí el juego y volvé a probar.", true);
            return;
        }
        HudModResult result;
        try
        {
            result = HudMod.Install(game, _gameProfiles);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            _host.ShowMessage(title, $"No se pudo instalar el mod.\n\n{e.Message}", true);
            return;
        }
        _host.ShowMessage(title, ModText(result), !result.Ok || result.Warnings.Count > 0);
    }

    internal static string ModText(HudModResult result)
    {
        var lines = new List<string>();
        if (result.Ok)
        {
            lines.Add($"Listo: el mod \"{HudMod.Name}\" quedó instalado.");
            lines.Add("");
            lines.AddRange(result.Installed);
            lines.Add("");
            lines.Add("Si el juego está abierto, cerralo y volvé a abrirlo. En Mods → Mods instalados tiene que figurar activo. "
                + "Después calibrá la pantalla de nuevo: los cuadrados de los recursos cambian de aspecto.");
        }
        if (result.Warnings.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add("Ojo:");
            lines.AddRange(result.Warnings.Select(w => $"• {w}"));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Dónde quedó la última captura de diagnóstico (o qué falló).</summary>
    [ObservableProperty] public partial string DiagnosticStatus { get; set; } = "";

    /// <summary>Cuánto espera antes de capturar: desde la app hay tiempo de volver al juego.</summary>
    public TimeSpan DiagnosticDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Captura de diagnóstico (botón y Ctrl+Alt+X): la pantalla entera sin pérdida y un .txt con lo que encontró la
    /// app, en %LOCALAPPDATA%\BoCoach\diagnostico, para mandar cuando algo no anda.
    /// </summary>
    [RelayCommand]
    private async Task Diagnostic()
    {
        DiagnosticStatus = $"Captura de diagnóstico en {DiagnosticDelay.TotalSeconds:0} s: volvé al juego…";
        await Task.Delay(DiagnosticDelay);
        try
        {
            var files = await DiagnosticCapture.SaveAsync(_screen, Layout, _regionsPath, _fonts(), _language(), _diagnosticsFolder,
                _findGame, _gameProfiles, DateTime.Now);
            DiagnosticStatus = $"Captura de diagnóstico guardada en {Path.GetDirectoryName(files.Image)} ({Path.GetFileNameWithoutExtension(files.Image)}): mandá esa carpeta.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or InvalidDataException)
        {
            DiagnosticStatus = $"No se pudo guardar la captura de diagnóstico: {e.Message}";
        }
    }

    /// <summary>Copia de la calibración manual que la calibración sola reemplaza (por si hay que volver a ella).</summary>
    public string PreviousRegionsPath => Path.ChangeExtension(_regionsPath, ".anterior.json");

    /// <summary>
    /// Calibración sola (hoja de ruta B4): busca el HUD del mod en la pantalla (los seis cuadrados negros con franja y el
    /// rectángulo negro del reloj) y, si lo encuentra, arma y guarda las regiones para esta resolución. Si no lo
    /// encuentra (sin el mod, juego cerrado), queda la calibración que había. La app la llama al empezar el coach.
    /// </summary>
    public bool TryAutoCalibrate()
    {
        var area = _screen.CaptureArea;
        CapturedImage screen;
        try
        {
            screen = _screen.Capture(area);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CalibrationStatus = $"No se pudo capturar la pantalla para calibrar sola: {e.Message}";
            return false;
        }
        if (AutoCalibration.Detect(screen, Layout) is not { } found)
        {
            CalibrationStatus = IsCalibrated
                ? $"No encontré el HUD del mod en la pantalla: sigue la calibración manual ({File.GetLastWriteTime(_regionsPath):d/M HH:mm})."
                : "No encontré el HUD del mod en la pantalla: se usan posiciones aproximadas (instalá el mod o calibrá a mano).";
            return false;
        }
        if (IsCalibrated)
            File.Copy(_regionsPath, PreviousRegionsPath, overwrite: true);
        found.Layout.Save(_regionsPath);
        // Con el HUD a otra escala, las capturas se llevan a 1080p antes de leerlas (ScreenFrames): es solo información.
        CalibrationStatus = $"Calibrada sola para {area.Width}×{area.Height}: {found.Squares.Count} cuadrados y el reloj"
            + (CapturedImage.NeedsResample(found.Scale) ? $", HUD a escala {found.Scale:0.00}" : "") + ".";
        return true;
    }

    /// <summary>La vista lo llama cada segundo para actualizar el estado de la grabación.</summary>
    public void Tick()
    {
        var area = _screen.CaptureArea;
        GameStatus = _screen.GameFound
            ? $"Juego abierto: {area.Width}×{area.Height} en ({area.X}, {area.Y})."
            : "Juego cerrado o minimizado.";

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
