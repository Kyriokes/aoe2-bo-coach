using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.Record;

/// <summary>Un registro esperando salir.</summary>
/// <param name="File">Nombre del archivo, en la carpeta de los registros.</param>
public sealed record QueuedRecord(string File, string GameId, double Length, DateTimeOffset Added, int Attempts, DateTimeOffset NextTry);

/// <summary>
/// Los registros por enviar (<c>cola.json</c>, en la carpeta de los registros). Tiene tope de espacio y de antigüedad, y
/// entre intentos una espera que crece. Sacar algo de la cola no borra el registro: es del jugador.
/// </summary>
public sealed class RecordQueue
{
    public const string FileName = "cola.json";

    /// <summary>Lo más grande que acepta la web.</summary>
    public const int MaxUploadBytes = 1024 * 1024;

    public const long DefaultMaxBytes = 50L * 1024 * 1024;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(3);

    private readonly string _folder;
    private readonly long _maxBytes;
    private readonly List<QueuedRecord> _items;

    public RecordQueue(string folder, long maxBytes = DefaultMaxBytes)
    {
        (_folder, _maxBytes) = (folder, maxBytes);
        _items = Load(Path.Combine(folder, FileName));
    }

    /// <summary>Lo que espera salir, lo más viejo primero.</summary>
    public IReadOnlyList<QueuedRecord> Pending
    {
        get
        {
            lock (_items)
                return _items.ToList();
        }
    }

    public string PathOf(QueuedRecord record) => Path.Combine(_folder, record.File);

    public void Enqueue(string path, string gameId, double length, DateTimeOffset now)
    {
        lock (_items)
        {
            if (_items.All(r => r.GameId != gameId))
                _items.Add(new QueuedRecord(Path.GetFileName(path), gameId, length, now, 0, now));
            Trim(now);
            Save();
        }
    }

    /// <summary>Lo que ya se puede intentar.</summary>
    public IReadOnlyList<QueuedRecord> Due(DateTimeOffset now)
    {
        lock (_items)
            return _items.Where(r => r.NextTry <= now).ToList();
    }

    /// <summary>Salió, o no va a salir nunca.</summary>
    public void Remove(QueuedRecord record)
    {
        lock (_items)
        {
            _items.RemoveAll(r => r.GameId == record.GameId);
            Save();
        }
    }

    /// <summary>No salió: otra vez más tarde, con el doble de espera cada vez.</summary>
    public void Failed(QueuedRecord record, DateTimeOffset now)
    {
        lock (_items)
        {
            var index = _items.FindIndex(r => r.GameId == record.GameId);
            if (index < 0)
                return;
            // Con un tope al exponente: de ahí en más la espera ya es la máxima.
            var doubled = BaseDelay * Math.Pow(2, Math.Min(record.Attempts, 16));
            _items[index] = record with { Attempts = record.Attempts + 1, NextTry = now + (doubled < MaxDelay ? doubled : MaxDelay) };
            Save();
        }
    }

    public void Clear()
    {
        lock (_items)
        {
            _items.Clear();
            Save();
        }
    }

    private void Trim(DateTimeOffset now)
    {
        _items.RemoveAll(r => now - r.Added > MaxAge);
        long Size(QueuedRecord r) => new FileInfo(PathOf(r)) is { Exists: true } file ? file.Length : 0;
        var total = _items.Sum(Size);
        while (total > _maxBytes && _items.Count > 0)
        {
            total -= Size(_items[0]);
            _items.RemoveAt(0);
        }
    }

    private static List<QueuedRecord> Load(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path), documentOptions: CoreJson.NoDuplicates) is not JsonArray array)
                return [];
            var items = new List<QueuedRecord>();
            foreach (var node in array.OfType<JsonObject>())
            {
                if (Text(node["file"]) is { } file && Text(node["game_id"]) is { } game && Date(node["added"]) is { } added && Date(node["next_try"]) is { } next)
                    items.Add(new QueuedRecord(Path.GetFileName(file), game, RawCapture.Double(node["length"]) ?? 0, added, RawCapture.Int(node["attempts"]) ?? 0, next));
            }
            return items;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return []; // una cola rota no impide nada: esos registros siguen en la carpeta, sin enviar
        }
    }

    private void Save()
    {
        var array = new JsonArray(_items.Select(r => (JsonNode?)new JsonObject
        {
            ["file"] = r.File,
            ["game_id"] = r.GameId,
            ["length"] = r.Length,
            ["added"] = r.Added.ToString("O", CultureInfo.InvariantCulture),
            ["attempts"] = r.Attempts,
            ["next_try"] = r.NextTry.ToString("O", CultureInfo.InvariantCulture),
        }).ToArray());
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, array.ToJsonString() + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    private static string? Text(JsonNode? node) => CoreJson.Text(node);

    private static DateTimeOffset? Date(JsonNode? node) =>
        Text(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
}
