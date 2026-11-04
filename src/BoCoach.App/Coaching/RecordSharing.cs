using BoCoach.Core;
using System.IO;
using BoCoach.Core.Record;
using BoCoach.Core.Web;

namespace BoCoach.App.Coaching;

/// <summary>
/// "Compartir mis partidas para mejorar el coach" (spec 2026-11-02): la cola de los registros por enviar, la identidad
/// de esta instalación (se crea recién cuando hace falta) y el pedido de borrado.
/// </summary>
/// <param name="folder">La carpeta de los registros (ahí queda la cola).</param>
/// <param name="identityPath">Dónde se guarda el identificador de la instalación.</param>
/// <param name="client">El cliente de la web; se pide en cada envío (la dirección se puede cambiar).</param>
public sealed class RecordSharing(string folder, string identityPath, Func<WebLibraryClient> client, string appVersion)
{
    /// <summary>Lo que se le dice al jugador, en la ventana y en "Primeros pasos".</summary>
    public static string Explanation => Lang.L(
        "Al terminar cada partida se sube un registro con números y eventos: lo que el coach leyó del HUD, lo que creyó y lo que te mostró, " +
        "con tu civ, el mapa y la BO. No viaja tu nombre, ni los de los otros jugadores, ni el chat, ni capturas de pantalla. " +
        "Sirve para encontrar lo que el coach lee o aconseja mal sin que nadie tenga que jugar de nuevo. " +
        "Si contás en el informe algo que anduvo mal, esa nota se sube también. " +
        "Podés apagarlo cuando quieras y pedir que se borre lo que ya subiste.",
        "When each match ends, a record with numbers and events is uploaded: what the coach read from the HUD, what it believed and what it showed you, " +
        "with your civ, the map and the BO. Your name, other players' names, the chat and screenshots are never sent. " +
        "It helps find what the coach reads or advises wrongly without anyone having to play again. " +
        "If you say in the report that something went wrong, that note is uploaded too. " +
        "You can turn it off whenever you want and ask for what you already uploaded to be deleted.");

    private readonly RecordQueue _queue = new(folder);
    private readonly SemaphoreSlim _sending = new(1, 1);

    /// <summary>Cuántos registros esperan salir.</summary>
    public int Pending => _queue.Pending.Count;

    public void Enqueue(GameRecordInfo info, DateTimeOffset now) => _queue.Enqueue(info.Path, info.GameId, info.Length, now);

    /// <summary>La nota del informe: un registro chico que apunta a su partida, a la cola como cualquier otro.</summary>
    public void AddNote(GameRecordInfo game, string text, DateTimeOffset now)
    {
        var path = GameRecordNote.PathFor(game.Path);
        var id = Guid.NewGuid().ToString("N");
        GameRecordNote.Write(path, id, game.GameId, now, appVersion, text);
        _queue.Enqueue(path, id, 0, now);
    }

    /// <summary>Manda lo vencido. Si ya hay un envío en curso no hace nada: lo nuevo sale en el siguiente.</summary>
    public async Task<RecordSendResult> SendPendingAsync(DateTimeOffset now, CancellationToken cancel = default)
    {
        if (!await _sending.WaitAsync(0, cancel))
            return new RecordSendResult(0, 0, 0);
        try
        {
            return await RecordUploader.SendPendingAsync(_queue, client(), InstallIdentity.LoadOrCreate(identityPath), appVersion, now, cancel);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>El pedido de borrado de lo que subió esta instalación. Devuelve cuántos registros se borraron.</summary>
    public Task<int> ForgetAsync(CancellationToken cancel = default) =>
        RecordUploader.ForgetAsync(_queue, client(), InstallIdentity.LoadOrCreate(identityPath), cancel);
}
