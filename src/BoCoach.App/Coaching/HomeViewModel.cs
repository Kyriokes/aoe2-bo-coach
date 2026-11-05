using BoCoach.Core;
using System.ComponentModel;
using BoCoach.App.Capture;
using BoCoach.App.Hotkeys;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Coaching;

/// <summary>Lo que la ventana hace por el tablero: abrir archivos, carpetas y el tutorial.</summary>
public interface IHomeHost
{
    void OpenFile(string path);

    void OpenFolder(string path);

    void ShowFirstRun();

    /// <summary>Una pregunta de sí o no.</summary>
    bool Confirm(string message);

    /// <summary>Cierra la app y la vuelve a abrir (cambio de idioma).</summary>
    void RestartApp();
}

/// <summary>Un idioma de la app en el selector.</summary>
public sealed record LanguageOption(string Code)
{
    public override string ToString() => Texts.LanguageName(Code);
}

/// <summary>
/// La ventana principal: junta el coach, la lista de BO y las herramientas, y arma el tablero
/// (<see cref="HomeScreen"/>) cada vez que cambia algo de lo que muestra.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IHomeHost _host;
    private HomeScreen? _preview;

    /// <summary>Los idiomas de la app (los mismos de la web).</summary>
    public IReadOnlyList<LanguageOption> AppLanguages { get; } = Lang.All.Select(code => new LanguageOption(code)).ToList();

    /// <summary>
    /// El idioma de la app. Cambiarlo queda guardado y pide reabrir, porque los textos se leen al cargar cada ventana; se
    /// pregunta porque puede haber una partida en curso.
    /// </summary>
    public LanguageOption AppLanguage
    {
        get => _appLanguage ??= new LanguageOption(Lang.Current);
        set
        {
            if (value == AppLanguage)
                return;
            _appLanguage = value;
            Coach.AppLanguage = value.Code;
            if (_host.Confirm(Texts.RestartForLanguage))
                _host.RestartApp();
        }
    }

    private LanguageOption? _appLanguage;

    public HomeViewModel(CoachSessionViewModel coach, BuildOrdersViewModel buildOrders, CaptureToolsViewModel tools, IHomeHost host)
    {
        Coach = coach;
        BuildOrders = buildOrders;
        Tools = tools;
        _host = host;
        Screen = HomeScreen.Build(Inputs());
        coach.PropertyChanged += OnChanged;
        coach.Live.PropertyChanged += OnChanged;
        buildOrders.PropertyChanged += OnChanged;
    }

    public CoachSessionViewModel Coach { get; }
    public BuildOrdersViewModel BuildOrders { get; }
    public CaptureToolsViewModel Tools { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTip))]
    [NotifyPropertyChangedFor(nameof(SecondaryTip))]
    [NotifyPropertyChangedFor(nameof(ShowLive))]
    public partial HomeScreen Screen { get; private set; }

    /// <summary>Si está abierto el proceso del juego (la ventana lo mira cada segundo).</summary>
    public bool GameOpen
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            Refresh();
        }
    }

    /// <summary>El panel "En la partida": mientras corre el coach.</summary>
    public bool ShowLive => Coach.IsRunning || _preview is { State: HomeState.InGame };

    public string? PrimaryTip => Tip(Screen.Primary);

    public string? SecondaryTip => Tip(Screen.Secondary);

    private static string? Tip(HomeButton? button) => button?.Action switch
    {
        HomeAction.StartStop => HotkeyTips.Instance.StartStop,
        HomeAction.Advance => HotkeyTips.Instance.Advance,
        HomeAction.Connect => Texts.ConnectAccountTip,
        _ => null,
    };

    /// <summary>Muestra un momento armado a mano, para las capturas sin el juego.</summary>
    internal void Preview(HomeInputs inputs)
    {
        _preview = HomeScreen.Build(inputs);
        Refresh();
    }

    private HomeInputs Inputs() => new()
    {
        Connected = BuildOrders.Connected,
        AccountText = BuildOrders.AccountText,
        AutoMode = Coach.IsAutoMode,
        SelectedName = BuildOrders.SelectedEntry?.Name,
        CoachRunning = Coach.IsRunning,
        AutoStart = Coach.AutoStart,
        GameOpen = GameOpen,
        InGame = Coach.InGame,
        GameTime = Coach.GameTime,
        Civ = Coach.GameCivName,
        Playing = Playing(),
        Report = Coach.LastReport,
        ReportPending = Coach.ReportPending,
        HasPlayerName = Coach.PlayerName.Trim().Length > 0,
    };

    /// <summary>Los dos primeros renglones de las líneas, o el paso si no hay líneas (BO terminada o abandonada).</summary>
    private string Playing() =>
        Coach.Live.Lanes.Count > 0 ? string.Join("   ·   ", Coach.Live.Lanes.Take(2)) : Coach.Live.Step;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
        // Sale de Screen, pero hay momentos en que el tablero es el mismo con el coach andando o no (partida terminada).
        if (e.PropertyName == nameof(CoachSessionViewModel.IsRunning))
            OnPropertyChanged(nameof(ShowLive));
    }

    private void Refresh() => Screen = _preview ?? HomeScreen.Build(Inputs());

    [RelayCommand]
    private void Run(HomeAction action)
    {
        switch (action)
        {
            case HomeAction.Connect:
                BuildOrders.ConnectCommand.Execute(null);
                break;
            case HomeAction.AccountSettings:
                BuildOrders.AccountSettingsCommand.Execute(null);
                break;
            case HomeAction.StartStop:
                Coach.StartStopCommand.Execute(null);
                break;
            case HomeAction.Advance:
                Coach.AdvanceCommand.Execute(null);
                break;
            case HomeAction.OpenReport:
                if (Coach.LastReport is { } report)
                    _host.OpenFile(report.Path);
                break;
            case HomeAction.OpenReports:
                OpenReports();
                break;
            case HomeAction.ReportSeen:
                Coach.MarkReportSeen();
                break;
            case HomeAction.FirstRun:
                _host.ShowFirstRun();
                break;
        }
    }

    /// <summary>La carpeta de los informes (de la tarjeta y de Ajustes).</summary>
    [RelayCommand]
    private void OpenReports()
    {
        System.IO.Directory.CreateDirectory(Coach.ReportsFolder);
        _host.OpenFolder(Coach.ReportsFolder);
    }
}
