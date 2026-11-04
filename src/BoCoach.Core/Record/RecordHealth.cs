using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.Record;

/// <summary>
/// Qué se leyó bien en una partida, sacado de su registro: lo primero que hay que mirar de lo que llega de otros jugadores.
/// Lo que se lee del HUD se mide sobre las capturas con reloj (las de sin reloj son menús, pausas o el final).
/// </summary>
public sealed record RecordHealth
{
    /// <summary>Cómo se guarda en el registro un renglón del log que no se reconoció (ver <c>EventMatcher.Scrub</c>).</summary>
    public const string Unrecognized = "?-";

    /// <summary>Cada cuántos segundos se intenta leer el panel de puntajes.</summary>
    private const double ScoresEverySeconds = 2;

    private const int DefaultIntervalMs = 500;

    public required string File { get; init; }
    public string? GameId { get; init; }
    public string? App { get; init; }
    public string? Civ { get; init; }
    public string? RivalCiv { get; init; }
    public string? Map { get; init; }
    public string? Language { get; init; }
    public string? Hud { get; init; }
    public double? HudScale { get; init; }
    public double Length { get; init; }
    public string? EndReason { get; init; }
    public bool Truncated { get; init; }
    public int Captures { get; init; }
    public double ClockRead { get; init; }
    public double StocksRead { get; init; }
    public double VillagersRead { get; init; }
    public double VillagersOnRead { get; init; }
    public double PopulationRead { get; init; }
    public double ScoresRead { get; init; }
    public int LogLines { get; init; }
    public int LogUnknown { get; init; }
    public int QueueSlots { get; init; }
    public int QueueUnknown { get; init; }
    public IReadOnlyDictionary<string, int> DoneBySource { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<double> Flags { get; init; } = [];

    /// <summary>Lo que el jugador contó de esta partida en el informe.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>La partida de la que habla, si este registro es una nota (ver <see cref="GameRecordNote"/>).</summary>
    public string? NoteFor { get; init; }

    public string? NoteText { get; init; }

    public string? Version => App?.Split(' ', 2)[0];

    public static RecordHealth From(GameRecordReader record, string file)
    {
        var meta = record.Meta;
        var (captures, withClock, stocks, villagers, villagersOn, population, scores) = (0, 0, 0, 0, 0, 0, 0);
        var (logLines, logUnknown, queueSlots, queueUnknown) = (0, 0, 0, 0);
        var done = new Dictionary<string, int>();
        var flags = new List<double>();
        double lastClock = 0;
        double? endLength = null;
        string? endReason = null;
        string? note = null;
        foreach (var line in record.Lines)
        {
            switch (Text(line["t"]))
            {
                case GameRecordKeys.Capture:
                    captures++;
                    if (line["log"] is JsonArray log)
                    {
                        logLines += log.Count;
                        logUnknown += log.Count(l => Text(l) == Unrecognized);
                    }
                    if (RawCapture.Double(line["clock"]) is not { } clock)
                        break;
                    lastClock = clock;
                    withClock++;
                    if (new[] { "food", "wood", "gold", "stone" }.All(k => RawCapture.Int(line[k]) is not null))
                        stocks++;
                    if (RawCapture.Int(line["vil"]) is not null)
                        villagers++;
                    if (line["on"] is JsonObject { Count: 4 })
                        villagersOn++;
                    if (RawCapture.Int(line["pop"]) is not null)
                        population++;
                    if (line["scores"] is JsonArray { Count: > 0 })
                        scores++;
                    if (line["queue"] is JsonArray queue)
                    {
                        queueSlots += queue.Count;
                        queueUnknown += queue.OfType<JsonArray>().Count(q => q.Count > 0 && Text(q[0]) == "?");
                    }
                    break;
                case GameRecordKeys.Done:
                    var source = Text(line["src"]) ?? "?";
                    done[source] = done.GetValueOrDefault(source) + 1;
                    break;
                case GameRecordKeys.Flag:
                    if (RawCapture.Double(line["time"]) is { } time)
                        flags.Add(time);
                    break;
                case GameRecordKeys.Note:
                    note = Text(line["text"]);
                    break;
                case GameRecordKeys.End:
                    endLength = RawCapture.Double(line["length"]);
                    endReason = Text(line["reason"]);
                    break;
            }
        }
        var intervalMs = RawCapture.Int(meta?["interval_ms"]) is { } interval and > 0 ? interval : DefaultIntervalMs;
        var scoreTries = withClock * intervalMs / (ScoresEverySeconds * 1000);
        return new RecordHealth
        {
            File = file,
            GameId = Text(meta?["game_id"]),
            App = Text(meta?["app"]),
            Civ = Text(meta?["civ"]),
            RivalCiv = Text(meta?["rival_civ"]),
            Map = Text(meta?["map"]),
            Language = Text(meta?["language"]),
            Hud = Text(meta?["hud"]),
            HudScale = RawCapture.Double(meta?["hud_scale"]),
            NoteFor = Text(meta?["note_for"]),
            NoteText = note,
            Length = endLength is > 0 ? endLength.Value : lastClock,
            EndReason = endReason,
            Truncated = record.Truncated,
            Captures = captures,
            ClockRead = Share(withClock, captures),
            StocksRead = Share(stocks, withClock),
            VillagersRead = Share(villagers, withClock),
            VillagersOnRead = Share(villagersOn, withClock),
            PopulationRead = Share(population, withClock),
            ScoresRead = scoreTries > 0 ? Math.Min(1, scores / scoreTries) : 0,
            LogLines = logLines,
            LogUnknown = logUnknown,
            QueueSlots = queueSlots,
            QueueUnknown = queueUnknown,
            DoneBySource = done,
            Flags = flags,
        };
    }

    private static double Share(int part, int total) => total > 0 ? (double)part / total : 0;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>El resumen de una carpeta de registros, con lo que manda cada jugador tratado como lo que es: datos de afuera.</summary>
public sealed class RecordHealthReport
{
    /// <summary>Lo que se lee por debajo de esto, o lo que no se reconoce por encima de lo otro, se señala.</summary>
    private const double WeakReading = 0.9;

    private const double WeakLog = 0.15;
    private const double WeakQueue = 0.25;

    /// <summary>Lo más largo que se muestra de un texto de la meta: nadie necesita más, y puede venir cualquier cosa.</summary>
    private const int MaxLabel = 60;

    private RecordHealthReport(IReadOnlyList<RecordHealth> games, IReadOnlyList<string> looseNotes, int unreadable)
    {
        (Games, LooseNotes, Unreadable) = (games, looseNotes, unreadable);
    }

    public IReadOnlyList<RecordHealth> Games { get; }

    /// <summary>Notas de partidas que no están en la carpeta.</summary>
    public IReadOnlyList<string> LooseNotes { get; }

    /// <summary>Archivos que no se pudieron leer como registro.</summary>
    public int Unreadable { get; }

    /// <summary>Lee todos los registros de la carpeta y sus subcarpetas (el panel de Supabase los guarda una por instalación).</summary>
    public static RecordHealthReport Scan(string folder)
    {
        var all = new List<RecordHealth>();
        var unreadable = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.jsonl.gz", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var record = GameRecordReader.Load(path);
                if (record.Meta is null)
                    unreadable++;
                else
                    all.Add(RecordHealth.From(record, Path.GetRelativePath(folder, path)));
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException or IOException or UnauthorizedAccessException)
            {
                unreadable++;
            }
        }
        var notes = all.Where(h => h.NoteFor is not null).ToList();
        var games = all.Where(h => h.NoteFor is null)
            .Select(g => g with { Notes = notes.Where(n => n.NoteFor == g.GameId && n.NoteText is not null).Select(n => n.NoteText!).ToList() })
            .ToList();
        var ids = games.Select(g => g.GameId).ToHashSet();
        var loose = notes.Where(n => !ids.Contains(n.NoteFor) && n.NoteText is not null).Select(n => n.NoteText!).ToList();
        return new RecordHealthReport(games, loose, unreadable);
    }

    public IReadOnlyList<string> Lines()
    {
        var noteCount = Games.Sum(g => g.Notes.Count) + LooseNotes.Count;
        var lines = new List<string>
        {
            $"{Count(Games.Count, "partida", "partidas")}, {Count(noteCount, "nota", "notas")}, {Count(Unreadable, "archivo roto", "archivos rotos")}",
        };
        Tally(lines, "versiones", g => g.Version);
        Tally(lines, "civs", g => g.Civ);
        Tally(lines, "mapas", g => g.Map);
        Tally(lines, "idiomas", g => g.Language);
        Tally(lines, "diseños del HUD", g => g.Hud);
        Tally(lines, "escalas del HUD", g => g.HudScale is { } scale ? Decimal(scale) : null);
        foreach (var game in Games)
            Describe(lines, game);
        if (LooseNotes.Count > 0)
        {
            lines.Add("");
            lines.Add("Notas de partidas que no están acá:");
            lines.AddRange(LooseNotes.Select(n => $"  nota: {Clean(n, GameRecordNote.MaxLength)}"));
        }
        return lines;
    }

    private void Tally(List<string> lines, string title, Func<RecordHealth, string?> key)
    {
        var groups = Games.GroupBy(g => Clean(key(g) ?? "(no dice)", MaxLabel))
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} ×{g.Count()}")
            .ToList();
        if (groups.Count > 0)
            lines.Add($"{title}: {string.Join(", ", groups)}");
    }

    private static void Describe(List<string> lines, RecordHealth game)
    {
        lines.Add("");
        var rival = game.RivalCiv is { } r ? $" contra {Clean(r, MaxLabel)}" : "";
        var end = game.Truncated ? "cortado" : game.EndReason is { } reason ? Clean(reason, MaxLabel) : "?";
        lines.Add($"{Clean(game.File, 200)}: {Label(game.Civ)}{rival}, {Label(game.Map)}, {Label(game.Language)}, {GameClock.Format(game.Length)} ({end}), "
            + $"{Label(game.Version)}, {Label(game.Hud)}, escala {(game.HudScale is { } scale ? Decimal(scale) : "?")}");
        lines.Add($"  lectura: reloj {Percent(game.ClockRead)}, recursos {Percent(game.StocksRead)}, aldeanos {Percent(game.VillagersRead)}, "
            + $"por recurso {Percent(game.VillagersOnRead)}, población {Percent(game.PopulationRead)}, puntajes {Percent(game.ScoresRead)}");
        lines.Add($"  log: {game.LogUnknown} de {game.LogLines} renglones sin reconocer; cola: {game.QueueUnknown} de {game.QueueSlots} lugares sin reconocer");
        if (game.DoneBySource.Count > 0)
            lines.Add("  se dio por hecho por: " + string.Join(", ", game.DoneBySource.OrderByDescending(d => d.Value).Select(d => $"{Clean(d.Key, MaxLabel)} {d.Value}")));
        if (game.Flags.Count > 0)
            lines.Add("  marcas: " + string.Join(", ", game.Flags.Select(GameClock.Format)));
        lines.AddRange(game.Notes.Select(n => $"  nota: {Clean(n, GameRecordNote.MaxLength)}"));
        var weak = Weak(game);
        if (weak.Count > 0)
            lines.Add("  FLOJO: " + string.Join(", ", weak));
    }

    private static List<string> Weak(RecordHealth game)
    {
        var weak = new List<string>();
        void Reading(string name, double share)
        {
            if (share < WeakReading)
                weak.Add($"{name} {Percent(share)}");
        }

        Reading("reloj", game.ClockRead);
        Reading("recursos", game.StocksRead);
        Reading("aldeanos", game.VillagersRead);
        // Sin el mod no hay de dónde leerlos: no es una falla.
        if (game.Hud is not null)
        {
            Reading("por recurso", game.VillagersOnRead);
            Reading("población", game.PopulationRead);
        }
        if (game.LogLines > 0 && (double)game.LogUnknown / game.LogLines > WeakLog)
            weak.Add($"log sin reconocer {Percent((double)game.LogUnknown / game.LogLines)}");
        if (game.QueueSlots > 0 && (double)game.QueueUnknown / game.QueueSlots > WeakQueue)
            weak.Add($"cola sin reconocer {Percent((double)game.QueueUnknown / game.QueueSlots)}");
        return weak;
    }

    private static string Label(string? text) => text is null ? "?" : Clean(text, MaxLabel);

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string Percent(double share) => $"{Math.Round(share * 100).ToString("0", CultureInfo.InvariantCulture)} %";

    private static string Decimal(double value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>En un solo renglón y sin caracteres de control: lo que escribió otro no puede mandarle órdenes a la terminal.</summary>
    private static string Clean(string text, int max)
    {
        var clean = new StringBuilder(Math.Min(text.Length, max));
        foreach (var c in text)
        {
            if (clean.Length >= max)
                break;
            if (c is '\n' or '\r' or '\t')
                clean.Append(' ');
            else if (!char.IsControl(c))
                clean.Append(c);
        }
        return clean.ToString();
    }
}
