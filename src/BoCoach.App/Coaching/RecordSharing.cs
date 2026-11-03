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
    public const string Explanation =
        "Al terminar cada partida se sube un registro con números y eventos: lo que el coach leyó del HUD, lo que creyó y lo que te mostró, " +
        "con tu civ, el mapa y la BO. No viaja tu nombre, ni los de los otros jugadores, ni el chat, ni capturas de pantalla. " +
        "Sirve para encontrar lo que el coach lee o aconseja mal sin que nadie tenga que jugar de nuevo. " +
        "Podés apagarlo cuando quieras y pedir que se borre lo que ya subiste.";

    private readonly RecordQueue _queue = new(folder);
    private readonly SemaphoreSlim _sending = new(1, 1);

    /// <summary>Cuántos registros esperan salir.</summary>
    public int Pending => _queue.Pending.Count;

    public void Enqueue(GameRecordInfo info, DateTimeOffset now) => _queue.Enqueue(info.Path, info.GameId, info.Length, now);

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
