using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App;

/// <summary>
/// Modo desarrollador (spec 2026-10-30, parte 2): lo de diagnóstico (calibrar a mano, grabar, captura de diagnóstico,
/// lo que lee el coach, sus atajos) solo se ve con el modo prendido. Se prende con <c>dev_mode</c> en settings.json o
/// con 5 clics sobre la versión. La ventana lo toma de <see cref="Instance"/>.
/// </summary>
public sealed partial class DevMode : ObservableObject
{
    public static DevMode Instance { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial bool IsOn { get; set; }

    /// <summary>"Grabar cada partida sola": solo actúa con el modo prendido.</summary>
    [ObservableProperty] public partial bool AutoRecord { get; set; }

    /// <summary>El encabezado del panel en vivo.</summary>
    public string Header => IsOn ? "Lo que ve el coach" : "En la partida";
}
