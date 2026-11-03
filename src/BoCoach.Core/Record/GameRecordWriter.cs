using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.Record;

/// <summary>
/// Escribe el registro de una partida (spec 2026-11-02): JSON Lines comprimido con gzip, una línea por objeto. La
/// primera es <c>meta</c>; <see cref="End"/> escribe la última (<c>end</c>) y <see cref="Dispose"/> la escribe como
/// "detenido" si nadie la escribió. <see cref="Flush"/> vuelca lo escrito al disco: un cierre brusco pierde solo lo de
/// después del último.
/// </summary>
public sealed class GameRecordWriter : IDisposable
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly StreamWriter _writer;
    private bool _ended;

    private GameRecordWriter(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    public string Path { get; }

    /// <summary>Cuántas líneas se escribieron (con la meta).</summary>
    public int Lines { get; private set; }

    public static GameRecordWriter Create(string path, JsonObject meta)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var gzip = new GZipStream(file, CompressionLevel.Optimal);
        var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" };
        var record = new GameRecordWriter(path, writer);
        meta["t"] = GameRecordKeys.Meta;
        meta["schema"] ??= GameRecordKeys.Schema;
        record.Write(meta);
        return record;
    }

    public void Write(JsonObject line)
    {
        if (_ended)
            return;
        _writer.WriteLine(line.ToJsonString(Compact));
        Lines++;
    }

    public void Flush() => _writer.Flush();

    /// <param name="reason">"fin" (30 s sin reloj), "nueva" (empezó otra partida) o "detenido".</param>
    /// <param name="length">Segundos de partida.</param>
    public void End(string reason, double length, double? ms = null, double? clock = null)
    {
        if (_ended)
            return;
        var line = new JsonObject { ["t"] = GameRecordKeys.End, ["reason"] = reason, ["length"] = Math.Round(length, 1) };
        if (ms is { } m)
            line["ms"] = m;
        if (clock is { } c)
            line["clock"] = c;
        Write(line);
        _ended = true;
    }

    public void Dispose()
    {
        End("detenido", 0);
        _writer.Dispose();
    }
}
