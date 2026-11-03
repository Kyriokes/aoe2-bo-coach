using BoCoach.Core;

namespace BoCoach.App.Coaching;

/// <summary>El momento en que está el jugador, y con eso el color y la acción del cuadro de estado.</summary>
public enum HomeState
{
    NoAccount,
    Waiting,
    InGame,
    Finished,
}

public enum HomeStepKind
{
    Done,
    Current,
    Todo,
    Warning,
    Info,
}

/// <summary>Lo que pueden hacer los botones y los enlaces del tablero.</summary>
public enum HomeAction
{
    Connect,
    AccountSettings,
    StartStop,
    Advance,
    OpenReport,
    OpenReports,
    ReportSeen,
    FirstRun,
}

public sealed record HomeButton(string Label, HomeAction Action);

/// <summary>Una de las cuatro tarjetas: Cuenta, Build order, Jugar e Informe.</summary>
public sealed record HomeStep(string Title, string Detail, HomeStepKind Kind, HomeButton? Link = null, HomeButton? SecondLink = null);

/// <summary>El último informe guardado. Summary y Length solo se saben del que se armó con la app abierta.</summary>
public sealed record ReportInfo(string Path, DateTime Date, string? Summary = null, double? Length = null);

/// <summary>Todo lo que hace falta saber para armar el tablero.</summary>
public sealed record HomeInputs
{
    public bool Connected { get; init; }
    public string AccountText { get; init; } = "";
    public bool AutoMode { get; init; } = true;

    /// <summary>La BO elegida de la lista; null si no hay.</summary>
    public string? SelectedName { get; init; }
    public bool CoachRunning { get; init; }
    public bool AutoStart { get; init; } = true;
    public bool GameOpen { get; init; }
    public bool InGame { get; init; }
    public double GameTime { get; init; }

    /// <summary>La civ de la partida en castellano; null si no se sabe.</summary>
    public string? Civ { get; init; }

    /// <summary>Lo que el coach está pidiendo en la partida.</summary>
    public string Playing { get; init; } = "";
    public ReportInfo? Report { get; init; }

    /// <summary>Terminó una partida y todavía no empezó otra (ni el jugador dijo "Listo").</summary>
    public bool ReportPending { get; init; }
    public bool HasPlayerName { get; init; } = true;
}

/// <summary>
/// El tablero de la ventana principal (spec 2026-11-02): el cuadro de estado y los cuatro pasos. Se calcula acá, sin
/// ventana, para que cada momento se pueda probar con un test y dibujar en una captura.
/// </summary>
public sealed record HomeScreen(HomeState State, string Title, string Detail, HomeButton? Primary, HomeButton? Secondary,
    HomeStep Account, HomeStep BuildOrder, HomeStep Play, HomeStep Report)
{
    public static HomeScreen Build(HomeInputs i)
    {
        var (state, title, detail, primary, secondary) = Status(i);
        return new HomeScreen(state, title, detail, primary, secondary, AccountStep(i), BuildOrderStep(i), PlayStep(i), ReportStep(i));
    }

    private static bool NeedsBuildOrder(HomeInputs i) => !i.AutoMode && i.SelectedName is null;

    private static HomeButton StartStop(HomeInputs i) =>
        new(i.CoachRunning ? Texts.StopCoach : Texts.StartNow, HomeAction.StartStop);

    private static (HomeState, string, string, HomeButton?, HomeButton?) Status(HomeInputs i)
    {
        if (i.InGame)
        {
            var clock = GameClock.Format(i.GameTime);
            return (HomeState.InGame, Texts.InGameTitle(clock, i.Civ), i.Playing.Length > 0 ? i.Playing : Texts.InGameFallback,
                new HomeButton(Texts.Advance, HomeAction.Advance), StartStop(i));
        }
        if (i.ReportPending && i.Report is { } report)
        {
            var title = report.Length is { } length ? Texts.FinishedTitle(GameClock.Format(length)) : Texts.ReportTitle;
            return (HomeState.Finished, title, report.Summary ?? Texts.FinishedFallback,
                new HomeButton(Texts.SeeReport, HomeAction.OpenReport), new HomeButton(Texts.ReportSeen, HomeAction.ReportSeen));
        }
        if (!i.Connected)
        {
            return (HomeState.NoAccount, Texts.NoAccountTitle, Texts.NoAccountDetail,
                new HomeButton(Texts.ConnectAccount, HomeAction.Connect), StartStop(i));
        }
        if (i.CoachRunning)
        {
            return (HomeState.Waiting, Texts.WaitingMatchTitle, i.GameOpen ? Texts.WaitingMatchGameOpen : Texts.WaitingMatchGameClosed,
                null, StartStop(i));
        }
        var start = StartStop(i);
        if (NeedsBuildOrder(i))
            return (HomeState.Waiting, i.AutoStart && !i.GameOpen ? Texts.WaitingGameTitle : Texts.StoppedTitle, Texts.NeedsBuildOrder, start, null);
        if (!i.AutoStart)
            return (HomeState.Waiting, Texts.StoppedTitle, Texts.StoppedAutoOff, start, null);
        // Con el coach automático y el juego abierto, que no corra es porque lo detuvo el jugador.
        return i.GameOpen
            ? (HomeState.Waiting, Texts.StoppedTitle, Texts.StoppedByYou, start, null)
            : (HomeState.Waiting, Texts.WaitingGameTitle, Texts.WaitingGameDetail, start, null);
    }

    private static HomeStep AccountStep(HomeInputs i) => i.Connected
        ? new HomeStep(Texts.StepAccount, i.AccountText, HomeStepKind.Done, new HomeButton(Texts.StepAccountLink, HomeAction.AccountSettings))
        : new HomeStep(Texts.StepAccount, Texts.StepAccountNone, HomeStepKind.Warning, new HomeButton(Texts.ConnectAccount, HomeAction.Connect),
            new HomeButton(Texts.StepAccountLink, HomeAction.AccountSettings));

    private static HomeStep BuildOrderStep(HomeInputs i) =>
        i.AutoMode ? new HomeStep(Texts.StepBuildOrder, Texts.StepBuildOrderAuto, i.Connected ? HomeStepKind.Done : HomeStepKind.Todo)
        : i.SelectedName is { } name ? new HomeStep(Texts.StepBuildOrder, Texts.StepBuildOrderManual(name), HomeStepKind.Done)
        : new HomeStep(Texts.StepBuildOrder, Texts.StepBuildOrderMissing, HomeStepKind.Warning);

    private static HomeStep PlayStep(HomeInputs i)
    {
        if (i.InGame)
            return new HomeStep(Texts.StepPlay, Texts.StepPlayInGame, HomeStepKind.Done);
        var firstRun = i.HasPlayerName ? null : new HomeButton(Texts.FirstRunButton, HomeAction.FirstRun);
        var detail = !i.HasPlayerName ? Texts.StepPlayNoName : i.GameOpen ? Texts.StepPlayGameOpen : Texts.StepPlayGameClosed;
        return new HomeStep(Texts.StepPlay, detail, !i.Connected || NeedsBuildOrder(i) ? HomeStepKind.Todo : HomeStepKind.Current, firstRun);
    }

    private static HomeStep ReportStep(HomeInputs i) => i.Report is { } report
        ? new HomeStep(Texts.StepReport, Texts.StepReportLast(report.Date, report.Length is { } length ? GameClock.Format(length) : null), i.ReportPending ? HomeStepKind.Done : HomeStepKind.Info,
            new HomeButton(Texts.StepReportOpen, HomeAction.OpenReport), new HomeButton(Texts.StepReportAll, HomeAction.OpenReports))
        : new HomeStep(Texts.StepReport, Texts.StepReportNone, HomeStepKind.Todo);
}
