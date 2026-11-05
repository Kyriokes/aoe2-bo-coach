using System.Text.Json.Nodes;

namespace BoCoach.Core.Record;

/// <summary>
/// La nota que el jugador escribe en el informe ("¿algo anduvo mal?"). Es un registro aparte, con su propio identificador
/// y <c>note_for</c> = la partida, porque el registro de la partida ya salió y la web no reemplaza uno que ya tiene. Para
/// la web es un registro más: pasa por la misma cola, los mismos topes y el mismo borrado.
/// </summary>
public static class GameRecordNote
{
    public const int MaxLength = 1000;

    private const string Extension = ".jsonl.gz";

    /// <summary>Al lado del registro de la partida, sin pisar una nota anterior de la misma partida.</summary>
    public static string PathFor(string gamePath)
    {
        var folder = Path.GetDirectoryName(gamePath) ?? "";
        var name = Path.GetFileName(gamePath);
        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^Extension.Length];
        var path = Path.Combine(folder, $"{name}-nota{Extension}");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name}-nota-{i}{Extension}");
        return path;
    }

    /// <param name="noteId">El identificador de este registro (32 hexadecimales, como el de una partida).</param>
    /// <param name="gameId">La partida de la que habla.</param>
    public static void Write(string path, string noteId, string gameId, DateTimeOffset now, string app, string text)
    {
        var clean = text.Trim();
        var meta = new JsonObject
        {
            ["t"] = GameRecordKeys.Meta,
            ["schema"] = GameRecordKeys.Schema,
            ["game_id"] = noteId,
            ["note_for"] = gameId,
            ["started"] = GameRecorder.Day(now),
            ["app"] = app,
        };
        using var writer = GameRecordWriter.Create(path, meta);
        writer.Write(new JsonObject { ["t"] = GameRecordKeys.Note, ["text"] = clean.Length > MaxLength ? clean[..MaxLength] : clean });
        writer.End("nota", 0);
    }
}
