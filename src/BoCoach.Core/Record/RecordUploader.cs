using System.Net;
using BoCoach.Core.Web;

namespace BoCoach.Core.Record;

/// <summary>Cuántos salieron, cuántos quedan para más tarde y cuántos se sacaron de la cola porque no van a salir nunca.</summary>
public sealed record RecordSendResult(int Sent, int Failed, int Dropped);

/// <summary>
/// Manda lo que está vencido en la cola. Se llama al terminar una partida y al abrir la app, nunca jugando. Si no hay
/// conexión o la web pide esperar, todo lo vencido espera (no se insiste con cada uno); lo que la web rechaza para
/// siempre (formato, tamaño, secreto) sale de la cola.
/// </summary>
public static class RecordUploader
{
    public static async Task<RecordSendResult> SendPendingAsync(RecordQueue queue, WebLibraryClient client, InstallIdentity identity, string appVersion,
        DateTimeOffset now, CancellationToken cancel = default)
    {
        var (sent, failed, dropped) = (0, 0, 0);
        var due = queue.Due(now);
        for (var i = 0; i < due.Count; i++)
        {
            var record = due[i];
            var file = new FileInfo(queue.PathOf(record));
            if (!file.Exists || file.Length > RecordQueue.MaxUploadBytes)
            {
                queue.Remove(record);
                dropped++;
                continue;
            }
            try
            {
                await client.UploadRecordAsync(await File.ReadAllBytesAsync(file.FullName, cancel), identity, record.GameId, appVersion, cancel);
                queue.Remove(record);
                sent++;
            }
            catch (WebLibraryException e) when (IsFinal(e.Status))
            {
                queue.Remove(record);
                dropped++;
            }
            catch (Exception e) when (e is WebLibraryException or IOException)
            {
                foreach (var waiting in due.Skip(i))
                {
                    queue.Failed(waiting, now);
                    failed++;
                }
                break;
            }
        }
        return new RecordSendResult(sent, failed, dropped);
    }

    /// <summary>El pedido de borrado: la web borra lo que subió esta instalación y lo que estaba por salir ya no sale. Devuelve cuántos se borraron.</summary>
    public static async Task<int> ForgetAsync(RecordQueue queue, WebLibraryClient client, InstallIdentity identity, CancellationToken cancel = default)
    {
        queue.Clear();
        return await client.ForgetRecordsAsync(identity, cancel);
    }

    private static bool IsFinal(HttpStatusCode? status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity;
}
