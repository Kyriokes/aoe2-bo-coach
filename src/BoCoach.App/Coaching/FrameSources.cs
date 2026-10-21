using System.Diagnostics;
using BoCoach.Vision.Capture;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

namespace BoCoach.App.Coaching;

/// <summary>Una captura: la imagen de cada región (se pide por nombre), cuándo se tomó y dónde está el juego.</summary>
/// <param name="Screen">
/// Un rectángulo cualquiera de la pantalla del juego (relativo a <paramref name="Area"/>), para la pantalla de carga;
/// null si la fuente no lo tiene (las grabaciones solo guardan las regiones).
/// </param>
/// <param name="InFront">False si el juego está abierto pero no al frente (alt-tab): no hay nada que leer.</param>
public sealed record Frame(Func<string, CapturedImage?> Region, double RealTime, PixelRect Area, Func<PixelRect, CapturedImage?>? Screen = null,
    bool InFront = true);

/// <summary>De dónde salen las capturas: la pantalla en vivo o una grabación.</summary>
public interface IFrameSource
{
    /// <summary>Cada cuánto se toma una captura.</summary>
    TimeSpan Interval { get; }

    /// <summary>Zona del juego en la pantalla (para ubicar el overlay).</summary>
    PixelRect Area { get; }

    /// <summary>La próxima captura, o null si no hay más (terminó la grabación).</summary>
    Frame? Next();

    /// <summary>
    /// Tamaño del HUD respecto de 1080p. Las capturas ya llegan llevadas a 1080p; lo que se mide en ellas (las franjas
    /// del mod) hay que multiplicarlo por esto para dibujarlo en la pantalla.
    /// </summary>
    double HudScale => 1;
}

/// <summary>La pantalla en vivo: cada región se captura una sola vez por captura, recién cuando se la pide.</summary>
public sealed class ScreenFrames(IScreenSource screen, RegionLayout layout) : IFrameSource
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(500);

    public PixelRect Area => screen.CaptureArea;

    public double HudScale => layout.HudScale;

    private PixelRect? _lastArea;

    public Frame? Next()
    {
        // Juego abierto pero atrás (alt-tab): una captura vacía, sin tocar la pantalla.
        if (!screen.GameInFront)
            return new Frame(_ => null, _clock.Elapsed.TotalSeconds, _lastArea ?? screen.CaptureArea, InFront: false);
        var area = screen.CaptureArea;
        _lastArea = area;
        var captured = new Dictionary<string, CapturedImage?>();
        return new Frame(
            region => captured.TryGetValue(region, out var image) ? image : captured[region] = Capture(region, area),
            _clock.Elapsed.TotalSeconds,
            area,
            rect => rect.IsEmpty ? null : screen.Capture(rect with { X = area.X + rect.X, Y = area.Y + rect.Y }));
    }

    private CapturedImage? Capture(string region, PixelRect area)
    {
        if (!layout.Regions.ContainsKey(region))
            return null;
        var rect = layout.PixelsOf(region, area);
        // Con el HUD a otra escala, la captura se lleva a los píxeles de 1080p de las letras y los íconos.
        return rect.IsEmpty ? null : screen.Capture(rect).AtHudScale(layout.HudScale);
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

    public double HudScale => session.HudScale;

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
