using System.Text.Json.Nodes;
using BoCoach.Core.Record;

namespace BoCoach.Core.Tests.Record;

/// <summary>"Algo anduvo mal": la marca adentro del registro y la nota del informe, que es un registro aparte.</summary>
public sealed class GameRecordNoteTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-nota-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static JsonObject Meta() => new() { ["t"] = GameRecordKeys.Meta, ["schema"] = 1, ["game_id"] = "abc" };

    [Fact]
    public void Una_marca_queda_en_el_registro_con_su_minuto()
    {
        var path = PathOf("partida.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
        {
            new GameRecorder(writer, TestData.GameData).Flag(125.44, 1000);
            writer.End("fin", 130);
        }

        var flag = Assert.Single(GameRecordReader.Load(path).Lines, l => l["t"]?.GetValue<string>() == GameRecordKeys.Flag);
        Assert.Equal(125.4, flag["time"]!.GetValue<double>());
        Assert.Equal(1000, flag["ms"]!.GetValue<double>());
    }

    // Va aparte porque el registro de la partida ya salió (la web no reemplaza uno que ya tiene) y para no tocar la base:
    // la web lo acepta como cualquier registro si la meta trae "t", "schema" y su propio "game_id".
    [Fact]
    public void Una_nota_es_un_registro_aparte_que_apunta_a_su_partida()
    {
        var path = PathOf("20261103-193834-nota.jsonl.gz");
        var noteId = new string('b', 32);
        var gameId = new string('a', 32);

        GameRecordNote.Write(path, noteId, gameId, DateTimeOffset.Parse("2026-11-04T10:00:00Z"), "v9.9", "  el coach pidió una casa de más \n");

        var record = GameRecordReader.Load(path);
        Assert.False(record.Truncated);
        Assert.Equal(GameRecordKeys.Meta, record.Meta!["t"]!.GetValue<string>());
        Assert.Equal(GameRecordKeys.Schema, record.Meta["schema"]!.GetValue<int>());
        Assert.Equal(noteId, record.Meta["game_id"]!.GetValue<string>());
        Assert.Equal(gameId, record.Meta["note_for"]!.GetValue<string>());
        Assert.Equal("v9.9", record.Meta["app"]!.GetValue<string>());
        var note = Assert.Single(record.Lines, l => l["t"]?.GetValue<string>() == GameRecordKeys.Note);
        Assert.Equal("el coach pidió una casa de más", note["text"]!.GetValue<string>());
    }

    [Fact]
    public void Una_nota_larga_se_corta()
    {
        var path = PathOf("nota.jsonl.gz");

        GameRecordNote.Write(path, "b", "a", DateTimeOffset.Now, "v9.9", new string('x', GameRecordNote.MaxLength + 500));

        var note = Assert.Single(GameRecordReader.Load(path).Lines, l => l["t"]?.GetValue<string>() == GameRecordKeys.Note);
        Assert.Equal(GameRecordNote.MaxLength, note["text"]!.GetValue<string>().Length);
    }

    [Fact]
    public void El_archivo_de_la_nota_sale_del_registro_y_no_pisa_otra()
    {
        var game = PathOf("20261103-193834.jsonl.gz");
        Assert.Equal(PathOf("20261103-193834-nota.jsonl.gz"), GameRecordNote.PathFor(game));

        File.WriteAllBytes(PathOf("20261103-193834-nota.jsonl.gz"), [1]);
        Assert.Equal(PathOf("20261103-193834-nota-2.jsonl.gz"), GameRecordNote.PathFor(game));
    }
}
