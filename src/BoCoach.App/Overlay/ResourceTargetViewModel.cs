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

    /// <summary>
    /// Con el diseño nuevo del mod, <see cref="Band"/> es el casillero del recurso (la GD va adentro, dejando su marco
    /// de color a la vista) o, en inactivos, la celda entera (el aviso es un marco alrededor).
    /// </summary>
    [ObservableProperty] public partial bool Chip { get; set; }

    /// <summary>Aviso que parpadea en rojo y amarillo (el de inactivos, mientras haya aldeanos sin hacer nada).</summary>
    [ObservableProperty] public partial bool Alert { get; set; }

    /// <summary>
    /// La GD como diferencia con los que hay (diseño mínimo, pedido del usuario del 2026-11-01): "=6" si están justos,
    /// "+2" si faltan 2, "-5" si sobran 5; sin saber cuántos hay, el objetivo solo. Null sin objetivo.
    /// </summary>
    [ObservableProperty] public partial string? Difference { get; set; }

    public void Apply(double? target, double? actual, bool estimated)
    {
        Target = target is { } t ? $"{t:0}" : null;
        Match = target is { } goal ? CoachText.Match(goal, actual) : null;
        Estimated = estimated && actual is not null;
        Actual = actual is { } a ? $"{(estimated ? "~" : "")}{a:0}" : null;
        Difference = DifferenceText(target, actual);
    }

    public static string? DifferenceText(double? target, double? actual)
    {
        if (target is not { } t)
            return null;
        if (actual is not { } a)
            return $"{t:0}";
        var diff = (int)Math.Round(t) - (int)Math.Round(a);
        return diff == 0 ? $"={t:0}" : diff > 0 ? $"+{diff}" : $"-{-diff}";
    }
}
