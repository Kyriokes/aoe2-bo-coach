using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Record;

/// <summary>Una captura del registro, con sus milisegundos reales.</summary>
public sealed record RecordedCapture(double Ms, RawCapture Capture);

/// <summary>
/// Lee un registro de partida entero. Un archivo cortado (la app se cerró sin terminar) o que pasa de los topes se lee
/// hasta donde llega (<see cref="Truncated"/>); una línea que no es JSON se saltea. Las claves y los tipos de línea que
/// no se conocen quedan en <see cref="Lines"/> tal cual.
/// </summary>
public sealed class GameRecordReader
{
    private GameRecordReader(JsonObject? meta, IReadOnlyList<JsonObject> lines, bool truncated)
    {
        Meta = meta;
        Lines = lines;
        Truncated = truncated;
    }

    public JsonObject? Meta { get; }

    /// <summary>Cada cuánto se miró la pantalla en esa partida.</summary>
    public int IntervalMs => RawCapture.Int(Meta?["interval_ms"]) is { } ms and > 0 ? ms : RecordHealth.DefaultIntervalMs;

    /// <summary>Todas las líneas menos la meta, en orden.</summary>
    public IReadOnlyList<JsonObject> Lines { get; }

    public bool Truncated { get; }

    /// <summary>La BO con que se jugó, o null si el registro no la trae (se jugó sin BO) o no se entiende.</summary>
    public BuildOrder? BuildOrder
    {
        get
        {
            try
            {
                return Meta?["build_order"] is JsonObject json ? BuildOrderSerializer.Parse(json.ToJsonString()) : null;
            }
            catch (Exception e) when (e is BuildOrderFormatException or JsonException or InvalidDataException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>Las capturas (<c>cap</c>) con sus renglones del log completos (los que no se repitieron, tomados de la anterior).</summary>
    public IEnumerable<RecordedCapture> Captures
    {
        get
        {
            IReadOnlyList<string>? log = null;
            foreach (var line in Lines)
            {
                if (CoreJson.Text(line["t"]) != GameRecordKeys.Capture)
                    continue;
                var capture = RawCapture.FromJson(line, log);
                log = capture.LogLines;
                yield return new RecordedCapture(RawCapture.Double(line["ms"]) ?? 0, capture);
            }
        }
    }

    public static bool IsRecord(string path) => path.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cuánto texto se lee de un registro. Lo puede mandar cualquiera y 1 MB de gzip puede dar 1 GB de texto; una partida
    /// de 19 minutos da 1 MB.
    /// </summary>
    internal const long MaxChars = 64L * 1024 * 1024;

    /// <summary>La línea más larga: la meta, con la BO (la web acepta hasta 512 KB).</summary>
    internal const int MaxLineChars = 1024 * 1024;

    /// <summary>Cuántas líneas se leen: sin esto, una línea corta ocupa mucho más en memoria que en el texto. 5 horas de partida son unas 50.000.</summary>
    internal const int MaxLines = 400_000;

    public static GameRecordReader Load(string path)
    {
        // Se puede leer mientras la app lo escribe (una partida en curso).
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        JsonObject? meta = null;
        var lines = new List<JsonObject>();
        var truncated = false;

        void Add(string text)
        {
            if (text.Length == 0)
                return;
            JsonObject? json;
            try
            {
                json = JsonNode.Parse(text, documentOptions: CoreJson.NoDuplicates) as JsonObject;
            }
            catch (JsonException)
            {
                json = null; // una línea a medias al final del archivo cortado
            }
            if (json is null)
                return;
            if (meta is null && CoreJson.Text(json["t"]) == GameRecordKeys.Meta)
                meta = json;
            else
                lines.Add(json);
        }

        try
        {
            var line = new StringBuilder();
            long chars = 0;
            var read = 0;
            for (var c = reader.Read(); c >= 0 && !truncated; c = reader.Read())
            {
                if (++chars > MaxChars)
                {
                    truncated = true;
                }
                else if (c != '\n')
                {
                    truncated = line.Length >= MaxLineChars;
                    line.Append((char)c);
                }
                else
                {
                    Add(line.ToString());
                    line.Clear();
                    truncated = ++read >= MaxLines;
                }
            }
            if (!truncated)
                Add(line.ToString()); // la última, si no terminó en un salto de línea
        }
        catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException)
        {
            truncated = true;
        }
        if (!truncated && (lines.Count == 0 || CoreJson.Text(lines[^1]["t"]) != GameRecordKeys.End))
            truncated = true;
        return new GameRecordReader(meta, lines, truncated);
    }
}
