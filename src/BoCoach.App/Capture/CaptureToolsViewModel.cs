using System.IO;
using System.Text.Json;
using BoCoach.App.Overlay;
using BoCoach.App.Web;
using BoCoach.Core;
using BoCoach.Core.Data;
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

    /// <summary>Una pregunta de sí o no.</summary>
    bool Confirm(string message);
}

/// <summary>Calibrar la pantalla, grabar partidas (RecordSession) e instalar el mod del HUD.</summary>
public sealed partial class CaptureToolsViewModel : ObservableObject, IRecordingControl, IDisposable
{
    private readonly IScreenSource _screen;
    private readonly ICaptureHost _host;
    private readonly string _regionsPath;
    private readonly string _recordingsRoot;
    private readonly Func<string?> _findGame;
    private readonly string _gameProfiles;
    private readonly Func<HudFonts> _fonts;
    private readonly Func<HudLayout> _layout;
    private readonly Func<GameLanguage> _language;
    private readonly Func<GameData?> _gameData;
    private readonly Func<string?> _playerName;
    private readonly string _diagnosticsFolder;
    private readonly LinkProtocol _links;
    private SessionRecorder? _recorder;

    public CaptureToolsViewModel(IScreenSource screen, ICaptureHost host, string? regionsPath = null, string? recordingsRoot = null,
        Func<string?>? findGame = null, string? gameProfiles = null,
        Func<HudFonts>? fonts = null, Func<GameLanguage>? language = null, string? diagnosticsFolder = null,
        Func<GameData?>? gameData = null, Func<string?>? playerName = null, Func<HudLayout>? layout = null, LinkProtocol? links = null)
    {
        _links = links ?? new LinkProtocol();
        _screen = screen;
        _host = host;
        _regionsPath = regionsPath ?? RegionLayout.DefaultPath;
        _recordingsRoot = recordingsRoot ?? SessionRecorder.DefaultOutputRoot;
        _findGame = findGame ?? GameIcons.FindGame;
        _gameProfiles = gameProfiles ?? HudMod.DefaultProfilesRoot;
        _fonts = fonts ?? (() => HudFonts.Load(Path.Combine(CoachData.DataDir, "fonts")));
        _layout = layout ?? (() => HudLayout.Load(Path.Combine(CoachData.DataDir, HudLayout.FileName)));
        _language = language ?? (() => GameLanguage.English);
        _gameData = gameData ?? (() => null);
        _playerName = playerName ?? (() => null);
        _diagnosticsFolder = diagnosticsFolder ?? DiagnosticCapture.DefaultFolder;
        UpdateCalibrationStatus();
    }

    [ObservableProperty] public partial string CalibrationStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    [NotifyPropertyChangedFor(nameof(RecordButtonVisible))]
    public partial bool IsRecording { get; set; }

    /// <summary>El botón de grabar es del modo desarrollador, pero con una grabación en curso se ve igual (para poder cortarla).</summary>
    public bool RecordButtonVisible => ShowsRecordButton(DevMode.Instance.IsOn, IsRecording);

    public static bool ShowsRecordButton(bool devMode, bool recording) => devMode || recording;

    /// <summary>La ventana avisa cuando se prende o apaga el modo desarrollador.</summary>
    public void DevModeChanged() => OnPropertyChanged(nameof(RecordButtonVisible));

    [ObservableProperty] public partial string RecordingStatus { get; set; } = "";

    /// <summary>Si se encontró la ventana del juego y de qué tamaño (se actualiza en <see cref="Tick"/>).</summary>
    [ObservableProperty] public partial string GameStatus { get; set; } = "";

    public string RecordButtonText => IsRecording ? Texts.RecordStop : Texts.RecordStart;

    public bool IsCalibrated => File.Exists(_regionsPath);

    /// <summary>
    /// Las regiones guardadas, o las de siempre. Un regions.json roto no puede impedir abrir la app: se aparta en
    /// <see cref="BrokenRegionsPath"/> y hay que volver a calibrar. Si solo no se puede leer ahora (lo tiene abierto otro
    /// programa), puede estar bien: no se toca.
    /// </summary>
    public RegionLayout Layout
    {
        get
        {
            try
            {
                return RegionLayout.LoadOrDefault(_regionsPath);
            }
            catch (Exception e) when (e is InvalidDataException or JsonException)
            {
                SetAsideBrokenRegions();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Sigue con las de siempre: la próxima lectura lo vuelve a probar.
            }
            return RegionLayout.Default;
        }
    }

    /// <summary>Adónde va un regions.json roto, para poder mirarlo.</summary>
    public string BrokenRegionsPath => Path.ChangeExtension(_regionsPath, ".roto.json");

    private bool _regionsSetAside;

    private void SetAsideBrokenRegions()
    {
        try
        {
            File.Move(_regionsPath, BrokenRegionsPath, overwrite: true);
            _regionsSetAside = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Queda donde está: se vuelve a probar en la próxima lectura.
        }
    }

    [RelayCommand]
    private void Calibrate()
    {
        if (_host.Calibrate(Layout) is not { } result)
            return;
        result.Save(_regionsPath);
        UpdateCalibrationStatus();
    }

    public void StartRecording()
    {
        if (!IsRecording)
            ToggleRecording();
    }

    public void StopRecording()
    {
        if (IsRecording)
            ToggleRecording();
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (_recorder is { IsRecording: true } recorder)
        {
            recorder.Stop();
            IsRecording = false;
            RecordingStatus = Texts.RecordingSaved(recorder.Frames, recorder.Folder);
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
    /// Arma el mod "BoCoach HUD" en los perfiles del juego (el panel de recursos del diseño data/hud_layout.json, con
    /// los números blancos sobre negro que mejor lee el coach) y cuenta cómo quedó.
    /// </summary>
    [RelayCommand]
    private void InstallMod()
    {
        var title = Texts.ModTitle;
        if (_findGame() is not { } game)
        {
            _host.ShowMessage(title, Texts.ModGameNotFound, true);
            return;
        }
        HudModResult result;
        try
        {
            result = HudMod.Install(game, _layout(), _gameProfiles);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            _host.ShowMessage(title, Texts.ModFailed(e.Message), true);
            return;
        }
        _host.ShowMessage(title, ModText(result), !result.Ok || result.Warnings.Count > 0);
    }

    /// <summary>
    /// Lo que la app dejó afuera de su carpeta: el mod en los perfiles del juego y la clave de los enlaces bocoach:// en el
    /// registro de Windows. Pregunta antes; lo demás del jugador no se toca.
    /// </summary>
    [RelayCommand]
    private void Uninstall()
    {
        if (!_host.Confirm(Texts.UninstallConfirm(HudMod.Name)))
            return;
        try
        {
            var profiles = HudMod.Uninstall(_gameProfiles);
            _links.Unregister();
            _host.ShowMessage(Texts.UninstallLabel, Texts.UninstallDone(profiles.Count), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _host.ShowMessage(Texts.UninstallLabel, Texts.UninstallFailed(e.Message), true);
        }
    }

    internal static string ModText(HudModResult result)
    {
        var lines = new List<string>();
        if (result.Ok)
        {
            lines.Add(Texts.ModInstalled(HudMod.Name));
            lines.Add("");
            lines.AddRange(result.Installed);
            lines.Add("");
            lines.Add(Texts.ModRestart);
        }
        if (result.Warnings.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add(Texts.ModWarnings);
            lines.AddRange(result.Warnings.Select(Texts.ModWarning));
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
        DiagnosticStatus = Texts.DiagnosticWaiting($"{DiagnosticDelay.TotalSeconds:0}");
        await Task.Delay(DiagnosticDelay);
        try
        {
            var files = await DiagnosticCapture.SaveAsync(_screen, Layout, _regionsPath, _fonts(), _language(), _diagnosticsFolder,
                _findGame, _gameProfiles, DateTime.Now, _gameData(), _playerName(), LayoutOrNull());
            DiagnosticStatus = Texts.DiagnosticSaved(Path.GetDirectoryName(files.Image), Path.GetFileNameWithoutExtension(files.Image));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or InvalidDataException)
        {
            DiagnosticStatus = Texts.DiagnosticFailed(e.Message);
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
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            // ArgumentException: con el juego minimizado, el rectángulo está vacío.
            CalibrationStatus = Texts.CalibrateCaptureFailed(e.Message);
            return false;
        }
        if (AutoCalibration.Detect(screen, Layout, LayoutOrNull()) is not { } found)
        {
            CalibrationStatus = IsCalibrated
                ? Texts.HudNotFoundManual(File.GetLastWriteTime(_regionsPath))
                : Texts.HudNotFoundDefault;
            return false;
        }
        try
        {
            SaveIfChanged(found.Layout);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CalibrationStatus = Texts.CalibrateNotSaved(e.Message);
            return false;
        }
        // Con el HUD a otra escala, las capturas se llevan a 1080p antes de leerlas (ScreenFrames): es solo información.
        CalibrationStatus = (found.Hud is { } hud
                ? Texts.CalibratedWithLayout(hud.Name, area.Width, area.Height, found.Squares.Count)
                : Texts.CalibratedAuto(area.Width, area.Height, found.Squares.Count))
            + (CapturedImage.NeedsResample(found.Scale) ? Texts.CalibratedScale($"{found.Scale:0.00}") : "") + ".";
        return true;
    }

    /// <summary>
    /// Guarda lo que encontró la calibración sola, si cambió algo. El coach la llama cada 5 s mientras no lee el reloj: sin
    /// esto, la copia de la calibración manual (<see cref="PreviousRegionsPath"/>) quedaba pisada con la automática.
    /// </summary>
    private void SaveIfChanged(RegionLayout layout)
    {
        var fresh = _regionsPath + ".nuevo";
        layout.Save(fresh);
        if (IsCalibrated)
        {
            if (File.ReadAllText(fresh) == File.ReadAllText(_regionsPath))
            {
                File.Delete(fresh);
                return;
            }
            File.Copy(_regionsPath, PreviousRegionsPath, overwrite: true);
        }
        File.Move(fresh, _regionsPath, overwrite: true);
    }

    /// <summary>El diseño del mod (data/hud_layout.json), o null si falta o está roto: la calibración sigue con el mod viejo.</summary>
    private HudLayout? LayoutOrNull()
    {
        try
        {
            return _layout();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>La vista lo llama cada segundo para actualizar el estado de la grabación.</summary>
    public void Tick()
    {
        var area = _screen.CaptureArea;
        GameStatus = _screen.GameFound
            ? Texts.GameOpen(area.Width, area.Height, area.X, area.Y)
            : Texts.GameClosed;

        if (_recorder is not { IsRecording: true } recorder)
            return;
        RecordingStatus = Texts.Recording(GameClock.Format(recorder.Elapsed.TotalSeconds), recorder.Frames, recorder.LastError);
    }

    public void Dispose() => _recorder?.Stop();

    private void UpdateCalibrationStatus()
    {
        // Primero se lee: si está roto, se aparta y ya no hay calibración.
        var layout = Layout;
        if (!IsCalibrated)
        {
            CalibrationStatus = _regionsSetAside ? Texts.RegionsSetAside(Path.GetFileName(BrokenRegionsPath)) : Texts.NotCalibrated;
            return;
        }
        CalibrationStatus = Texts.Calibrated(layout.ReferenceWidth, layout.ReferenceHeight, File.GetLastWriteTime(_regionsPath));
    }
}
