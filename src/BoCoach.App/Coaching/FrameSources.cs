using System.Diagnostics;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Coaching;

/// <summary>Una captura: la imagen de cada región (se pide por nombre), cuándo se tomó y dónde está el juego.</summary>
public sealed record Frame(Func<string, CapturedImage?> Region, double RealTime, PixelRect Area);

/// <summary>De dónde salen las capturas: la pantalla en vivo o una grabación.</summary>
public interface IFrameSource
{
    /// <summary>Cada cuánto se toma una captura.</summary>
    TimeSpan Interval { get; }

    /// <summary>Zona del juego en la pantalla (para ubicar el overlay).</summary>
    PixelRect Area { get; }

    /// <summary>La próxima captura, o null si no hay más (terminó la grabación).</summary>
    Frame? Next();
}

/// <summary>La pantalla en vivo: cada región se captura una sola vez por captura, recién cuando se la pide.</summary>
public sealed class ScreenFrames(IScreenSource screen, RegionLayout layout) : IFrameSource
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(500);

    public PixelRect Area => screen.CaptureArea;

    public Frame? Next()
    {
        var area = screen.CaptureArea;
        var captured = new Dictionary<string, CapturedImage?>();
        return new Frame(
            region => captured.TryGetValue(region, out var image) ? image : captured[region] = Capture(region, area),
            _clock.Elapsed.TotalSeconds,
            area);
    }

    private CapturedImage? Capture(string region, PixelRect area)
    {
        if (!layout.Regions.ContainsKey(region))
            return null;
        var rect = layout.PixelsOf(region, area);
        return rect.IsEmpty ? null : screen.Capture(rect);
    }
}

/// <summary>
/// Una grabación, captura por captura, con el tiempo grabado. <paramref name="speed"/> acelera la reproducción
/// sin saltear capturas (el log necesita verlas todas).
/// </summary>
public sealed class RecordedFrames(RecordedSession session, double speed = 1) : IFrameSource
{
    private int _next;

    public TimeSpan Interval => TimeSpan.FromMilliseconds(500 / Math.Max(0.1, speed));

    public PixelRect Area { get; init; } = GameWindow.PrimaryScreen;

    /// <summary>Número de la última captura entregada.</summary>
    public int Current { get; private set; } = -1;

    public Frame? Next()
    {
        if (_next >= session.Frames.Count)
            return null;
        var frame = session.Frames[_next++];
        Current = frame.Index;
        return new Frame(
            region => session.Has(frame.Index, region) ? session.Image(frame.Index, region) : null,
            frame.TimeMs / 1000.0,
            Area);
    }
}
