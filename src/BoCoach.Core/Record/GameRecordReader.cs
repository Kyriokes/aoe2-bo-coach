using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Record;

/// <summary>Una captura del registro, con sus milisegundos reales.</summary>
public sealed record RecordedCapture(double Ms, RawCapture Capture);

/// <summary>
/// Lee un registro de partida entero. Un archivo cortado (la app se cerró sin terminar) se lee hasta donde llega
/// (<see cref="Truncated"/>); una línea que no es JSON se saltea. Las claves y los tipos de línea que no se conocen
/// quedan en <see cref="Lines"/> tal cual.
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
            catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException)
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
                if (line["t"]?.GetValue<string>() != GameRecordKeys.Capture)
                    continue;
                var capture = RawCapture.FromJson(line, log);
                log = capture.LogLines;
                yield return new RecordedCapture(RawCapture.Double(line["ms"]) ?? 0, capture);
            }
        }
    }

    public static bool IsRecord(string path) => path.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    public static GameRecordReader Load(string path)
    {
        // Se puede leer mientras la app lo escribe (una partida en curso).
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        JsonObject? meta = null;
        var lines = new List<JsonObject>();
        var truncated = false;
        try
        {
            while (reader.ReadLine() is { } text)
            {
                if (text.Length == 0)
                    continue;
                JsonObject? json;
                try
                {
                    json = JsonNode.Parse(text) as JsonObject;
                }
                catch (JsonException)
                {
                    json = null; // una línea a medias al final del archivo cortado
                }
                if (json is null)
                    continue;
                if (meta is null && json["t"]?.GetValue<string>() == GameRecordKeys.Meta)
                    meta = json;
                else
                    lines.Add(json);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException)
        {
            truncated = true;
        }
        if (!truncated && (lines.Count == 0 || lines[^1]["t"]?.GetValue<string>() != GameRecordKeys.End))
            truncated = true;
        return new GameRecordReader(meta, lines, truncated);
    }
}
