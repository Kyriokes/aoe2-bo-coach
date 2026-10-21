using System.Collections.ObjectModel;
using BoCoach.Core.Data;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Loading;
using BoCoach.Vision.Ocr;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.FirstRun;

/// <summary>
/// Tutorial de primer uso (hoja de ruta C4, idea del usuario del 2026-10-20): idioma del juego, el mod del HUD y una
/// partida rápida contra 7 IA. Mientras dura, la app mira la pantalla cada 2 s: en la pantalla de carga lee la lista
/// de jugadores (el jugador elige su nombre de ahí) y, ya en la partida, se calibra sola con el HUD del mod.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>Cada cuánto se mira la pantalla mientras se espera la partida.</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);

    private readonly IScreenSource _screen;
    private readonly GameData _data;
    private readonly Func<GameLanguage> _language;
    private readonly Func<GameLanguage, OcrReader?> _createOcr;
    private readonly Func<bool> _autoCalibrate;
    private readonly Action<string> _savePlayerName;
    private LoadingScreenReader? _reader;
    private GameLanguage? _readerLanguage;
    private bool _busy;

    /// <param name="screen">La pantalla (sigue a la ventana del juego).</param>
    /// <param name="language">El idioma del juego elegido (para las civs de la pantalla de carga y el OCR).</param>
    /// <param name="autoCalibrate">La calibración sola: true si encontró el HUD del mod y guardó las regiones.</param>
    /// <param name="savePlayerName">Guarda "Tu nombre" en los ajustes.</param>
    /// <param name="createOcr">El OCR para un idioma (por defecto, el de Windows).</param>
    public FirstRunViewModel(IScreenSource screen, GameData data, Func<GameLanguage> language, Func<bool> autoCalibrate,
        Action<string> savePlayerName, string currentName = "", Func<GameLanguage, OcrReader?>? createOcr = null)
    {
        _screen = screen;
        _data = data;
        _language = language;
        _autoCalibrate = autoCalibrate;
        _savePlayerName = savePlayerName;
        _createOcr = createOcr ?? (l => l.CreateOcr());
        PlayerName = currentName;
    }

    /// <summary>Si la app está mirando la pantalla (desde "Ya estoy en el juego" hasta tener los jugadores y la calibración).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchButtonText))]
    public partial bool Watching { get; set; }

    public string WatchButtonText => Watching ? "Dejar de mirar" : "Mirar la pantalla";

    [ObservableProperty] public partial string WatchStatus { get; set; } = "";

    /// <summary>Los jugadores leídos en la pantalla de carga.</summary>
    public ObservableCollection<LoadingPlayer> Players { get; } = [];

    public bool HasPlayers => Players.Count > 0;

    [ObservableProperty] public partial LoadingPlayer? SelectedPlayer { get; set; }

    /// <summary>Al elegir un jugador de la lista, su nombre pasa a la caja (se puede corregir: el OCR a veces erra una letra).</summary>
    partial void OnSelectedPlayerChanged(LoadingPlayer? value)
    {
        if (value is not null)
            PlayerName = value.Name;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCommand))]
    public partial string PlayerName { get; set; } = "";

    /// <summary>Si la calibración sola ya encontró el HUD del mod en esta pasada del tutorial.</summary>
    [ObservableProperty] public partial bool Calibrated { get; set; }

    [ObservableProperty] public partial string CalibrationStatus { get; set; } = "Todavía no vi el HUD del mod.";

    [RelayCommand]
    private void ToggleWatching()
    {
        Watching = !Watching;
        WatchStatus = Watching
            ? "Mirando la pantalla cada 2 s. Empezá la partida rápida: la lista de jugadores sale en la pantalla de carga."
            : Players.Count > 0 ? WatchStatus : "";
    }

    /// <summary>
    /// Una mirada a la pantalla (la ventana la llama cada <see cref="WatchInterval"/> mientras <see cref="Watching"/>):
    /// primero la pantalla de carga, después el HUD para calibrar. Cuando tiene las dos cosas deja de mirar.
    /// </summary>
    public async Task TickAsync()
    {
        if (!Watching || _busy)
            return;
        _busy = true;
        try
        {
            if (Players.Count == 0)
                await ReadLoadingScreenAsync();
            if (!Calibrated && _autoCalibrate())
            {
                Calibrated = true;
                CalibrationStatus = "Calibrada sola con el HUD del mod.";
            }
            if (Players.Count > 0 && Calibrated)
            {
                Watching = false;
                WatchStatus = "Listo: ya leí la pantalla de carga y calibré la pantalla. Podés salir de la partida.";
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            WatchStatus = $"No pude capturar la pantalla: {e.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReadLoadingScreenAsync()
    {
        var language = _language();
        if (_reader is null || _readerLanguage != language)
        {
            if (_createOcr(language) is not { } ocr)
            {
                WatchStatus = "Windows no tiene el reconocimiento de texto (OCR) instalado: no puedo leer la pantalla de carga.";
                return;
            }
            (_reader, _readerLanguage) = (new LoadingScreenReader(ocr, _data, language.Code), language);
        }
        var area = _screen.CaptureArea;
        if (await _reader.ReadPanelAsync(_screen.Capture(LoadingScreen.Panel(area.Width, area.Height))) is not { } reading)
            return;
        foreach (var player in reading.Players)
            Players.Add(player);
        OnPropertyChanged(nameof(HasPlayers));
        SelectedPlayer = Guess(reading, PlayerName);
        WatchStatus = $"Leí la pantalla de carga: {reading.Players.Count} jugadores. Elegí tu nombre en la lista."
            + (Calibrated ? "" : " Cuando empiece la partida, me calibro solo con el HUD.");
    }

    /// <summary>
    /// Quién es el jugador: el del nombre que ya tenía la app, si está; si no, el jugador 1 (en la partida rápida
    /// contra la IA el que la arma es el 1, como en la captura del usuario), o el primero de la lista.
    /// </summary>
    public static LoadingPlayer? Guess(LoadingReading reading, string? currentName) =>
        reading.Find(currentName) ?? reading.Players.FirstOrDefault(p => p.Slot == 1) ?? reading.Players.FirstOrDefault();

    private bool CanFinish() => PlayerName.Trim().Length > 0;

    /// <summary>Guarda el nombre (lo que importa del tutorial: con eso el coach toma la civ en cada pantalla de carga).</summary>
    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish()
    {
        _savePlayerName(PlayerName.Trim());
        Watching = false;
        Finished = true;
    }

    /// <summary>Si se tocó "Guardar mi nombre" (la ventana se cierra).</summary>
    [ObservableProperty] public partial bool Finished { get; set; }
}
