using System.Globalization;
using System.Text.Json;
using BoCoach.Vision.Capture;

namespace BoCoach.Vision.Recording;

/// <summary>Una captura de una grabación: su número y los milisegundos desde que empezó a grabar.</summary>
public sealed record RecordedFrame(int Index, long TimeMs);

/// <summary>Lee una grabación hecha por <see cref="SessionRecorder"/> para correr el pipeline sin el juego.</summary>
public sealed class RecordedSession
{
    private RecordedSession(string folder, IReadOnlyList<RecordedFrame> frames, IReadOnlyList<string> regions)
    {
        Folder = folder;
        Frames = frames;
        Regions = regions;
    }

    public string Folder { get; }
    public IReadOnlyList<RecordedFrame> Frames { get; }

    /// <summary>Regiones que se grabaron (según session.json).</summary>
    public IReadOnlyList<string> Regions { get; }

    public static RecordedSession Load(string folder)
    {
        var index = Path.Combine(folder, "frames.csv");
        if (!File.Exists(index))
            throw new FileNotFoundException($"{folder} no parece una grabación: falta frames.csv.", index);

        var frames = File.ReadLines(index)
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(','))
            .Select(p => new RecordedFrame(int.Parse(p[0], CultureInfo.InvariantCulture), long.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();

        using var session = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "session.json")));
        var regions = session.RootElement.GetProperty("regions").EnumerateObject().Select(p => p.Name).ToList();
        return new RecordedSession(folder, frames, regions);
    }

    public bool Has(int frame, string region) => File.Exists(PathOf(frame, region));

    public CapturedImage Image(int frame, string region) => Png.Read(PathOf(frame, region));

    private string PathOf(int frame, string region) => Path.Combine(Folder, SessionRecorder.FileName(frame, region));
}
