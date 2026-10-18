using BoCoach.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App.Overlay;

/// <summary>
/// El número grande debajo de un recurso del HUD: cuántos aldeanos tendría que haber ahí, en verde si coincide con los
/// que hay, rojo si faltan y amarillo si sobran.
/// </summary>
public sealed partial class ResourceTargetViewModel(string region) : ObservableObject
{
    /// <summary>La región calibrada del recurso (<see cref="Vision.Regions.RegionNames"/>), para ubicar el número.</summary>
    public string Region { get; } = region;

    [ObservableProperty] public partial string? Target { get; set; }
    [ObservableProperty] public partial TargetMatch? Match { get; set; }

    /// <summary>Los que hay salen de la estimación de la economía, no del HUD: se marca con "~".</summary>
    [ObservableProperty] public partial bool Estimated { get; set; }

    /// <summary>"hay 12" para el panel de la app.</summary>
    [ObservableProperty] public partial string? Actual { get; set; }

    /// <summary>
    /// Con el mod: la franja de color del cuadrado del recurso, en píxeles de su caja calibrada. La GD se dibuja ahí,
    /// chica, y no tapa la cola de producción del juego (que aparece debajo del panel). null = debajo de la caja.
    /// </summary>
    [ObservableProperty] public partial Vision.Capture.PixelRect? Band { get; set; }

    public void Apply(double? target, double? actual, bool estimated)
    {
        Target = target is { } t ? $"{t:0}" : null;
        Match = target is { } goal ? CoachText.Match(goal, actual) : null;
        Estimated = estimated && actual is not null;
        Actual = actual is { } a ? $"{(estimated ? "~" : "")}{a:0}" : null;
    }
}
