using System.Globalization;
using System.Net;
using System.Text;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using static BoCoach.Core.Lang;

namespace BoCoach.Core.Engine;

/// <summary>Un paso de la BO: cuándo lo terminaba el plan de la BO, cuándo se terminó y cuánto atraso.</summary>
public sealed record StepTiming(int Number, string Text, double? Planned, double? Done)
{
    /// <summary>
    /// Lo que el juego no avisa (granjas, mover aldeanos): su hora es la del paso posterior que la dio por hecha o la de
    /// cuando se dejó de pedir, no la real, así que no tiene atraso (en la partida 20261031-163414 las granjas salían
    /// 14:30 tarde).
    /// </summary>
    public bool Unannounced { get; init; }

    public double? Delay => !Unannounced && Planned is { } p && Done is { } d ? d - p : null;
}

/// <summary>Un tramo de partida (desde, cuánto duró).</summary>
public sealed record GameInterval(double Start, double Duration);

/// <summary>Un recurso acumulado sin gastar: desde cuándo, cuánto duró y hasta cuánto llegó.</summary>
public sealed record FloatingResource(Resource Resource, double Start, double Duration, int Peak);

/// <summary>El clic de una edad: cuándo fue, cuándo se llegó, el objetivo de la BO y lo que daba su plan.</summary>
public sealed record AgeClick(string Age, double? Click, double? Arrived, double? Target, double? Planned);

/// <summary>
/// El informe de después de la partida (D5, contenido elegido por el usuario el 2026-10-20): tiempos contra la BO,
/// centro urbano parado, inactivos y trabas por población, y recursos acumulados.
/// </summary>
public sealed record GameReport(
    string BuildOrder,
    double Length,
    IReadOnlyList<StepTiming> Steps,
    IReadOnlyList<AgeClick> AgeClicks,
    double TownCenterIdle,
    IReadOnlyList<GameInterval> TownCenterGaps,
    double IdleVillagersTime,
    double HousedTime,
    int HousedTimes,
    IReadOnlyList<FloatingResource> Floating)
{
    /// <summary>D10: el cruce de la pantalla de carga (<see cref="MatchupText.Report"/>); vacío si no se vio.</summary>
    public IReadOnlyList<string> Matchup { get; init; } = [];
}

/// <summary>
/// Junta el informe captura por captura (lo llama el <see cref="Coach"/> en cada snapshot). Los tiempos del plan salen
/// de simular la BO entera desde el arranque estándar de su civ, como la vista previa del editor.
/// </summary>
public sealed class GameReportBuilder(BuildOrder bo, Planner planner)
{
    /// <summary>Stock desde el que un recurso cuenta como acumulado.</summary>
    public const int FloatingAmount = 500;

    /// <summary>Segundos que tiene que durar lo acumulado para entrar en el informe.</summary>
    public const double FloatingSeconds = 60;

    /// <summary>Los huecos del centro urbano más cortos que esto no se listan (sí suman al total).</summary>
    public const double GapSeconds = 10;

    private readonly PlanResult _plan = planner.Simulate(bo);
    private double? _start;
    private double _last;
    private double _tcIdle;
    private double? _tcIdleSince;
    private readonly List<GameInterval> _tcGaps = [];
    private double _idleTime;
    private double _housedTime;
    private int _housedTimes;
    private bool _housed;
    private readonly Dictionary<Resource, (double Since, int Peak)> _floatingSince = [];
    private readonly List<FloatingResource> _floating = [];
    private IReadOnlyList<StepProgress> _steps = [];

    // El clic de cada edad mientras está en curso: al terminar, el paso ya no lo guarda.
    private readonly Dictionary<string, double> _clicks = [];

    public void Observe(GameSnapshot snapshot, CoachView view, PlayerState state, IReadOnlyList<StepProgress> steps)
    {
        var now = snapshot.Time;
        var first = _start is null;
        _start ??= now;
        var dt = first ? 0 : Math.Clamp(now - _last, 0, 5); // un hueco sin capturas no suma de más
        _last = now;
        _steps = steps;
        foreach (var s in steps.Where(s => s.State == StepState.InProgress && s.StartedAt is not null && s.Step.What is not null))
            _clicks.TryAdd(s.Step.What!, s.StartedAt!.Value);

        // Cada hueco cuenta desde que paró (el aviso sale después de un margen), no desde el aviso.
        if (view.TownCenterIdle is { } idle)
        {
            _tcIdleSince ??= now - idle;
        }
        else if (_tcIdleSince is { } since)
        {
            _tcIdle += now - since;
            if (now - since >= GapSeconds)
                _tcGaps.Add(new GameInterval(since, now - since));
            _tcIdleSince = null;
        }

        if (snapshot.IdleVillagers > 0)
            _idleTime += dt;

        var housed = state.PopulationCap < BuildOrderValidator.MaxPopulation && state.Population >= state.PopulationCap;
        if (housed)
            _housedTime += dt;
        if (housed && !_housed)
            _housedTimes++;
        _housed = housed;

        foreach (var resource in Enum.GetValues<Resource>())
        {
            var amount = (int)snapshot.Stock[resource];
            if (amount >= FloatingAmount)
            {
                var (since, peak) = _floatingSince.TryGetValue(resource, out var f) ? f : (now, 0);
                _floatingSince[resource] = (since, Math.Max(peak, amount));
            }
            else if (_floatingSince.Remove(resource, out var f) && now - f.Since >= FloatingSeconds)
            {
                _floating.Add(new FloatingResource(resource, f.Since, now - f.Since, f.Peak));
            }
        }
    }

    public GameReport Build()
    {
        var gaps = _tcGaps.ToList();
        var tcIdle = _tcIdle;
        if (_tcIdleSince is { } open)
        {
            tcIdle += _last - open;
            if (_last - open >= GapSeconds)
                gaps.Add(new GameInterval(open, _last - open));
        }
        var floating = _floating.Concat(_floatingSince.Where(f => _last - f.Value.Since >= FloatingSeconds)
            .Select(f => new FloatingResource(f.Key, f.Value.Since, _last - f.Value.Since, f.Value.Peak))).ToList();

        // Contra cuándo lo termina la BO, no contra cuándo lo arranca: en la 20261104-095625 "6 aldeanos en ovejas" salía
        // 1:16 tarde hecho a tiempo, y las 2 casas 32 s tarde (lo que tardan en construirse).
        var steps = _steps.Select(s => new StepTiming(s.Index + 1, StepText.Describe(s.Step), _plan.For(s.Step.Id) is { } planned ? planned.End ?? planned.Start : null,
            s.State == StepState.Done ? s.DoneAt : null)
        {
            Unannounced = s.Step.Do == StepAction.Move || (EventConventions.EventFor(s.Step) is { } id && !StateBelief.CanObserve(id)),
        }).ToList();
        var clicks = new List<AgeClick>();
        foreach (var (tech, name, target) in new[] { ("feudal_age", "Feudal", bo.Targets?.FeudalClickTime), ("castle_age", "Castillos", bo.Targets?.CastleClickTime) })
        {
            var step = _steps.FirstOrDefault(s => s.Step is { Do: StepAction.Research } && s.Step.What == tech);
            if (step is null)
                continue;
            clicks.Add(new AgeClick(name, _clicks.TryGetValue(tech, out var click) ? click : step.StartedAt,
                step.State == StepState.Done ? step.DoneAt : null, target, _plan.ClickTime(tech)));
        }
        return new GameReport(bo.Name, _last - (_start ?? 0), steps, clicks, tcIdle, gaps, _idleTime, _housedTime, _housedTimes, floating);
    }
}

/// <summary>Los textos del informe: un resumen en renglones (la ventana) y una página HTML (el archivo).</summary>
public static class GameReportText
{
    private static string Clock(double? seconds) => seconds is { } s ? GameClock.Format(s) : "-";

    private static string Span(double seconds) => seconds < 60 ? $"{seconds:0} s" : GameClock.Format(seconds);

    private static string Resource(Resource r) => r switch
    {
        Data.Resource.Food => L("comida", "food"),
        Data.Resource.Wood => L("madera", "wood"),
        Data.Resource.Gold => L("oro", "gold"),
        _ => L("piedra", "stone"),
    };

    /// <summary>El resumen: lo más importante de cada parte.</summary>
    public static IReadOnlyList<string> Summary(GameReport report)
    {
        var lines = new List<string> { L($"{report.BuildOrder}: {GameClock.Format(report.Length)} de partida.", $"{report.BuildOrder}: {GameClock.Format(report.Length)} game.") };
        foreach (var click in report.AgeClicks)
        {
            var target = click.Target is { } t ? L($", objetivo {Clock(t)}", $", target {Clock(t)}") : "";
            lines.Add(click switch
            {
                { Click: { } c } => L($"Clic de {click.Age} a las {Clock(c)}{target} (la BO bien jugada: {Clock(click.Planned)}).", $"{click.Age} click at {Clock(c)}{target} (the BO played well: {Clock(click.Planned)})."),
                { Arrived: { } a } => L($"Llegaste a {click.Age} a las {Clock(a)} (el clic no se vio){target}.", $"You reached {click.Age} at {Clock(a)} (the click wasn't seen){target}."),
                _ => L($"No hiciste clic en {click.Age}.", $"You didn't click {click.Age}."),
            });
        }
        var late = report.Steps.Where(s => s.Delay is > 60).OrderByDescending(s => s.Delay).Take(3).ToList();
        if (late.Count > 0)
            lines.Add(L($"Lo que más se atrasó: {string.Join("; ", late.Select(s => $"{s.Text} ({Span(s.Delay!.Value)} tarde)"))}.", $"What fell behind the most: {string.Join("; ", late.Select(s => $"{s.Text} ({Span(s.Delay!.Value)} late)"))}."));
        var notDone = report.Steps.Count(s => s.Done is null);
        if (notDone > 0)
            lines.Add(L($"Pasos sin hacer: {notDone} de {report.Steps.Count}.", $"Steps not done: {notDone} of {report.Steps.Count}."));
        lines.Add(report.TownCenterIdle >= 1
            ? L($"Centro urbano parado: {Span(report.TownCenterIdle)} en total{(report.TownCenterGaps.Count > 0 ? $"; el hueco más largo, {Span(report.TownCenterGaps.Max(g => g.Duration))} desde las {Clock(report.TownCenterGaps.MaxBy(g => g.Duration)!.Start)}" : "")}.",
                $"Town center idle: {Span(report.TownCenterIdle)} in total{(report.TownCenterGaps.Count > 0 ? $"; the longest gap, {Span(report.TownCenterGaps.Max(g => g.Duration))} from {Clock(report.TownCenterGaps.MaxBy(g => g.Duration)!.Start)}" : "")}.")
            : L("El centro urbano no paró.", "The town center never stopped."));
        lines.Add(IdleAndHoused(report));
        foreach (var f in report.Floating.OrderByDescending(f => f.Duration).Take(3))
            lines.Add(L($"Acumulaste {Resource(f.Resource)}: {Span(f.Duration)} con más de {GameReportBuilder.FloatingAmount} desde las {Clock(f.Start)} (hasta {f.Peak}).",
                $"You floated {Resource(f.Resource)}: {Span(f.Duration)} over {GameReportBuilder.FloatingAmount} from {Clock(f.Start)} (up to {f.Peak})."));
        // D10: la sección "El cruce" (spec 5.4).
        lines.AddRange(report.Matchup.Select((line, i) => i == 0 ? L("El cruce: ", "The matchup: ") + line : line));
        return lines;
    }

    private static string IdleAndHoused(GameReport report) => L(
        $"Aldeanos inactivos: {Span(report.IdleVillagersTime)}. Sin lugar por población: {Span(report.HousedTime)} ({report.HousedTimes} {(report.HousedTimes == 1 ? "vez" : "veces")}).",
        $"Idle villagers: {Span(report.IdleVillagersTime)}. Housed: {Span(report.HousedTime)} ({report.HousedTimes} {(report.HousedTimes == 1 ? "time" : "times")}).");

    /// <summary>La página del informe completo, sin nada de afuera (se abre en el navegador).</summary>
    public static string Html(GameReport report, DateTime date)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html lang=\"{Current}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{L("Informe", "Report")}: {E(report.BuildOrder)}</title><style>");
        sb.Append("body{font-family:Segoe UI,system-ui,sans-serif;max-width:900px;margin:24px auto;padding:0 16px;background:#1e1a16;color:#f4f2ee}");
        sb.Append("h1{font-size:22px}h2{font-size:17px;color:#e8c26a;margin-top:28px}table{border-collapse:collapse;width:100%;font-size:14px}");
        sb.Append("td,th{border-bottom:1px solid #5c5046;padding:4px 8px;text-align:left}td.n{text-align:right;font-variant-numeric:tabular-nums}");
        sb.Append(".late{color:#f05a50}.ok{color:#6fd86f}.muted{color:#a39a8f}</style></head><body>");
        sb.Append($"<h1>{E(report.BuildOrder)}</h1><p class=\"muted\">{date:yyyy-MM-dd HH:mm} · {GameClock.Format(report.Length)}{L(" de partida", "")}</p>");
        sb.Append($"<h2>{L("Resumen", "Summary")}</h2><ul>");
        foreach (var line in Summary(report with { Matchup = [] }).Skip(1))
            sb.Append($"<li>{E(line)}</li>");
        if (report.Matchup.Count > 0)
        {
            sb.Append($"</ul><h2>{L("El cruce", "The matchup")}</h2><ul>");
            foreach (var line in report.Matchup)
                sb.Append($"<li>{E(line)}</li>");
        }
        sb.Append(L("</ul><h2>Pasos contra la BO</h2><table><tr><th>#</th><th>Paso</th><th>La BO</th><th>Vos</th><th>Atraso</th></tr>",
            "</ul><h2>Steps against the BO</h2><table><tr><th>#</th><th>Step</th><th>The BO</th><th>You</th><th>Delay</th></tr>"));
        foreach (var s in report.Steps)
        {
            var delay = s.Delay is { } d ? $"<span class=\"{(d > 60 ? "late" : "ok")}\">{(d >= 0 ? "+" : "-")}{Span(Math.Abs(d))}</span>" : (s.Done is null ? L("sin hacer", "not done") : "-");
            sb.Append($"<tr><td class=\"n\">{s.Number}</td><td>{E(s.Text)}</td><td class=\"n\">{Clock(s.Planned)}</td><td class=\"n\">{Clock(s.Done)}</td><td class=\"n\">{delay}</td></tr>");
        }
        sb.Append($"</table><h2>{L("Centro urbano parado", "Town center idle")}</h2>");
        sb.Append(report.TownCenterGaps.Count == 0
            ? L($"<p>{Span(report.TownCenterIdle)} en total, sin huecos de más de {GameReportBuilder.GapSeconds:0} s.</p>", $"<p>{Span(report.TownCenterIdle)} in total, no gaps over {GameReportBuilder.GapSeconds:0} s.</p>")
            : L($"<p>{Span(report.TownCenterIdle)} en total.</p><table><tr><th>Desde</th><th>Duró</th></tr>", $"<p>{Span(report.TownCenterIdle)} in total.</p><table><tr><th>From</th><th>Lasted</th></tr>") +
              string.Concat(report.TownCenterGaps.Select(g => $"<tr><td class=\"n\">{Clock(g.Start)}</td><td class=\"n\">{Span(g.Duration)}</td></tr>")) + "</table>");
        sb.Append($"<h2>{L("Inactivos y casas", "Idle villagers and houses")}</h2><p>{E(IdleAndHoused(report))}</p>");
        sb.Append($"<h2>{L("Recursos acumulados", "Floating resources")}</h2>");
        sb.Append(report.Floating.Count == 0
            ? L($"<p>Nunca más de {GameReportBuilder.FloatingAmount} de algo durante un minuto.</p>", $"<p>Never over {GameReportBuilder.FloatingAmount} of anything for a minute.</p>")
            : L("<table><tr><th>Recurso</th><th>Desde</th><th>Duró</th><th>Hasta</th></tr>", "<table><tr><th>Resource</th><th>From</th><th>Lasted</th><th>Up to</th></tr>") +
              string.Concat(report.Floating.Select(f => $"<tr><td>{Resource(f.Resource)}</td><td class=\"n\">{Clock(f.Start)}</td><td class=\"n\">{Span(f.Duration)}</td><td class=\"n\">{f.Peak.ToString(CultureInfo.InvariantCulture)}</td></tr>")) + "</table>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
