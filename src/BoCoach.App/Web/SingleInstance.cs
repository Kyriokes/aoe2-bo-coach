using System.IO;
using System.IO.Pipes;
using System.Text;

namespace BoCoach.App.Web;

/// <summary>
/// Una sola app abierta por usuario: si se abre un enlace bocoach:// con la app ya abierta, el enlace le llega a esa
/// ventana (por un pipe con nombre) en vez de abrir otra.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Lo máximo que se lee de un enlace (un bocoach://import/&lt;código&gt; tiene menos de 40 caracteres).</summary>
    private const int MaxLinkLength = 4096;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>
    /// La instancia principal, o null si ya hay otra abierta (en ese caso, si hay <paramref name="link"/>, se le
    /// manda y esta tiene que cerrarse).
    /// </summary>
    public static SingleInstance? Claim(string? link, string name = "BoCoach.App")
    {
        // Alcanza con saber si el mutex ya existía (lo creó otra instancia): no hace falta tomarlo.
        var mutex = new Mutex(initiallyOwned: false, $@"Local\{name}", out var createdNew);
        var pipeName = PipeNameFor(name);
        if (createdNew)
            return new SingleInstance(mutex, pipeName);

        mutex.Dispose();
        if (link is not null)
            TrySend(pipeName, link);
        return null;
    }

    /// <summary>El nombre del pipe de esa instancia: uno por usuario de Windows.</summary>
    internal static string PipeNameFor(string name) => $"{name}.{Environment.UserName}";

    /// <summary>Cuánto se espera antes de volver a abrir el pipe cuando falló (otro proceso lo tiene, se cortó).</summary>
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Escucha los enlaces que mandan otras instancias (llama a <paramref name="onLink"/> fuera del hilo de la interfaz).</summary>
    public void Listen(Action<string> onLink) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                if (await ReadLinkAsync(server, _stop.Token) is { Length: > 0 } link)
                    onLink(link);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Se cortó la conexión, o el pipe no se pudo abrir (otro proceso tiene ese nombre, "All pipe instances
                // are busy"): se espera un poco y se vuelve a escuchar. Antes solo se atrapaba IOException y cualquier
                // otro error terminaba el escucha en silencio: la app seguía abierta pero los enlaces se perdían.
                try
                {
                    await Task.Delay(RetryDelay, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    });

    /// <summary>
    /// El primer renglón del pipe, cortado en <see cref="MaxLinkLength"/>: así otro proceso no puede hacer crecer la
    /// memoria de la app mandando sin fin (ReadLineAsync leía hasta el salto de línea, sin tope).
    /// </summary>
    private static async Task<string> ReadLinkAsync(Stream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe);
        var buffer = new char[256];
        var link = new StringBuilder();
        while (link.Length < MaxLinkLength)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token);
            if (read == 0)
                break;
            var newline = Array.IndexOf(buffer, '\n', 0, read);
            link.Append(buffer, 0, newline >= 0 ? newline : read);
            if (newline >= 0)
                break;
        }
        return link.ToString().TrimEnd('\r');
    }

    private static void TrySend(string pipeName, string link)
    {
        try
        {
            // CurrentUserOnly en los dos lados: el pipe solo acepta procesos del mismo usuario de Windows, y el cliente
            // solo le habla a un pipe creado por ese usuario (otro no puede hacerse pasar por la app y recibir enlaces).
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            // 10 s: con la PC cargada (los tests de .NET y de la web a la vez) 3 s no alcanzaban y el enlace se perdía.
            client.Connect(timeout: 10_000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(link);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // La otra instancia no responde: el enlace se pierde, pero no hay que abrir otra ventana.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.Dispose();
    }
}
