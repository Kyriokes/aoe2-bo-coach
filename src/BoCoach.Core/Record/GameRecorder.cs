using System.Globalization;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Record;

/// <summary>
/// Arma las líneas del registro de una partida con lo que el coach ya calcula: las capturas crudas, lo que la creencia
/// dio por terminado, lo que cree, lo que muestra el overlay (solo cuando cambia) y el pronóstico de recursos a 60 s con
/// lo que pasó. Solo números, eventos y textos del coach: ningún nombre de jugador.
/// </summary>
public sealed class GameRecorder(GameRecordWriter writer, GameData data)
{
    private readonly List<(double Time, double At, JsonArray Pred)> _forecasts = [];
    private IReadOnlyList<string>? _lastLog;
    private string? _lastShow;
    private string? _lastBelief;
    private double _nextForecast;

    /// <summary>Cada cuántos segundos de partida se pronostica.</summary>
    public double ForecastEvery { get; init; } = 10;

    /// <summary>A cuántos segundos se pronostica.</summary>
    public double ForecastHorizon { get; init; } = 60;

    /// <summary>
    /// La BO tal como se jugó, sin el autor (es un nombre) ni el código. Igual lleva a la cuenta: la app solo juega BO de
    /// la lista de la cuenta conectada, las propias (una privada solo la baja su dueño) y las sumadas (la base guarda quién
    /// sumó cada una), así que quien lee los registros puede buscar los pasos en la base y dar con la cuenta. Sacarle el
    /// nombre o las notas no cambia eso. Tampoco hace falta la BO: la app pide la lista con el token desde la misma
    /// conexión con que sube los registros. La página de privacidad dice los dos caminos. Va adentro del registro porque
    /// sin ella la partida no se puede reproducir: otro jugador puede tenerla privada, y una publicada se puede republicar
    /// distinta (partida 20261102-212823).
    /// </summary>
    private static JsonNode? PlayedBuildOrder(BuildOrder? bo)
    {
        if (bo is null || bo.IsNone)
            return null;
        var json = JsonNode.Parse(BuildOrderSerializer.Serialize(bo))?.AsObject();
        json?.Remove("author");
        json?.Remove("id");
        return json;
    }

    /// <summary>
    /// El día, en UTC: alcanza para ordenar los registros sin que el archivo diga a qué hora se juega ni en qué huso se vive.
    /// La web igual sabe a qué hora llega cada registro (se sube al terminar la partida), y la página de privacidad lo dice.
    /// </summary>
    public static string Day(DateTimeOffset when) => when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// La línea <c>meta</c> de una partida: lo que hace falta para reproducirla, sin nombres ni la cuenta (la BO jugada, y
    /// la conexión con que se sube, sí pueden llevar a ella: ver <see cref="PlayedBuildOrder"/>).
    /// </summary>
    /// <param name="map">La clave del mapa en data/maps.json, o null: lo que lee el OCR es texto libre y no va tal cual.</param>
    /// <param name="buildOrder">La BO con que se jugó.</param>
    public static JsonObject Meta(string gameId, DateTimeOffset started, string app, string gameData, string? hud, double hudScale, string civ,
        string? map, string? rivalCiv, int? slot, string language, int intervalMs, BuildOrder? buildOrder = null) => new()
        {
            ["build_order"] = PlayedBuildOrder(buildOrder),
            ["t"] = GameRecordKeys.Meta,
            ["schema"] = GameRecordKeys.Schema,
            ["game_id"] = gameId,
            ["started"] = Day(started),
            ["app"] = app,
            ["game_data"] = gameData,
            ["hud"] = hud,
            ["hud_scale"] = hudScale,
            ["civ"] = civ,
            ["map"] = map,
            ["rival_civ"] = rivalCiv,
            ["slot"] = slot,
            ["language"] = language,
            ["interval_ms"] = intervalMs,
        };

    /// <summary>Una captura, tenga o no reloj. <paramref name="ms"/>: milisegundos reales desde que se empezó a leer.</summary>
    public void Capture(RawCapture raw, double ms)
    {
        var changed = !SameLines(raw.LogLines, _lastLog);
        _lastLog = raw.LogLines;
        var line = raw.ToJson(logChanged: changed);
        line["ms"] = Math.Round(ms);
        writer.Write(line);
    }

    /// <summary>Después de cada <see cref="Coach.Update"/>: lo que terminó, lo que se cree, lo que se muestra y los pronósticos.</summary>
    public void Update(GameSnapshot snapshot, Coach coach, SnapshotBuilder builder, double ms)
    {
        var time = Math.Round(snapshot.Time, 1);
        foreach (var done in builder.LastCompletions)
            Write(GameRecordKeys.Done, ms, time, new JsonObject { ["id"] = done.EventId, ["src"] = done.Source.ToString(), ["str"] = done.Strength.ToString() });
        if (snapshot.Belief is { } belief)
            Belief(belief, ms, time);
        Show(coach.View, ms, time);
        Forecasts(snapshot, coach, ms, time);
    }

    /// <summary>"Algo anduvo mal": el jugador marcó este momento. Lo que pasaba se ve en las líneas de alrededor.</summary>
    public void Flag(double time, double ms) => Write(GameRecordKeys.Flag, ms, Math.Round(time, 1), []);

    public void Flush() => writer.Flush();

    private void Write(string type, double ms, double time, JsonObject line)
    {
        line["t"] = type;
        line["ms"] = Math.Round(ms);
        line["time"] = time;
        writer.Write(line);
    }

    private void Belief(BeliefView belief, double ms, double time)
    {
        var line = new JsonObject
        {
            ["buildings"] = Counts(belief.Buildings),
            ["research"] = new JsonObject(belief.Research.OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => KeyValuePair.Create(r.Key, (JsonNode?)(r.Value == ResearchStatus.Done ? "done" : "running")))),
            ["units"] = Counts(belief.Units),
            ["age"] = belief.Age.ToString(),
            ["built_cap"] = belief.BuiltCap,
        };
        var key = line.ToJsonString();
        if (key == _lastBelief)
            return;
        _lastBelief = key;
        Write(GameRecordKeys.Belief, ms, time, line);
    }

    private static JsonObject Counts(IReadOnlyDictionary<string, int> counts) =>
        new(counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value)));

    private void Show(CoachView view, double ms, double time)
    {
        var lines = CoachText.Lines(view, data);
        var line = new JsonObject
        {
            ["critical"] = lines.Critical,
            ["critical_kind"] = lines.Critical is null ? null : lines.CriticalKind.ToString(),
            ["lanes"] = lines.LanesShown
                ? new JsonArray(lines.Lanes.Select(l => (JsonNode?)new JsonArray(l.Lane.ToString(), l.Label, l.Text, l.Tone.ToString())).ToArray())
                : null,
            ["current"] = lines.LanesShown ? null : lines.Current,
            ["running"] = new JsonArray(lines.Running.Select(r => (JsonNode?)r).ToArray()),
            ["suggestion"] = lines.Suggestion,
            ["alert"] = lines.Alert,
            ["idle"] = lines.Idle,
            ["deviation"] = lines.Deviation,
            ["card"] = lines.Compact ? new JsonArray(lines.Card.Select(c => (JsonNode?)c.Text).ToArray()) : null,
            ["gd"] = Amounts(view.Distribution),
            ["actual"] = Amounts(view.Actual),
            ["house"] = view.NeedsHouse,
            ["margin"] = view.HouseMargin,
            ["abandoned"] = view.Abandoned,
        };
        // Los segundos de centro urbano parado cambian en cada captura: no alcanzan para escribir otra línea.
        var key = line.ToJsonString();
        if (key == _lastShow)
            return;
        _lastShow = key;
        line["tc_idle"] = view.TownCenterIdle is { } idle ? Math.Round(idle) : null;
        Write(GameRecordKeys.Show, ms, time, line);
    }

    private void Forecasts(GameSnapshot snapshot, Coach coach, double ms, double time)
    {
        foreach (var (made, _, predicted) in _forecasts.Where(f => time >= f.At))
            Write(GameRecordKeys.Outcome, ms, time, new JsonObject { ["forecast"] = made, ["pred"] = predicted.DeepClone(), ["actual"] = Amounts(snapshot.Stock) });
        _forecasts.RemoveAll(f => time >= f.At);
        if (snapshot.Time < _nextForecast || coach.View.Actual is not { } on || coach.Economy is not { } economy)
            return;
        _nextForecast = snapshot.Time + ForecastEvery;
        var rates = economy.NominalRates;
        var pred = Amounts(new ResourceAmounts(
            snapshot.Stock.Food + ForecastHorizon * on.Food * rates.Food,
            snapshot.Stock.Wood + ForecastHorizon * on.Wood * rates.Wood,
            snapshot.Stock.Stone + ForecastHorizon * on.Stone * rates.Stone,
            snapshot.Stock.Gold + ForecastHorizon * on.Gold * rates.Gold))!;
        var target = Math.Round(time + ForecastHorizon, 1);
        _forecasts.Add((time, target, pred));
        Write(GameRecordKeys.Forecast, ms, time, new JsonObject
        {
            ["at"] = target,
            ["stock"] = Amounts(snapshot.Stock),
            ["on"] = Amounts(on),
            ["on_hud"] = coach.View.ActualFromHud,
            ["rates"] = new JsonArray(Math.Round(rates.Food, 3), Math.Round(rates.Wood, 3), Math.Round(rates.Stone, 3), Math.Round(rates.Gold, 3)),
            ["pred"] = pred.DeepClone(),
        });
    }

    /// <summary>[comida, madera, piedra, oro].</summary>
    private static JsonArray? Amounts(ResourceAmounts? amounts) => amounts is { } a
        ? new JsonArray(Math.Round(a.Food, 1), Math.Round(a.Wood, 1), Math.Round(a.Stone, 1), Math.Round(a.Gold, 1))
        : null;

    private static bool SameLines(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));
}
