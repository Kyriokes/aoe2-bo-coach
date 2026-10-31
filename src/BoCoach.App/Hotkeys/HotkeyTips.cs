using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App.Hotkeys;

/// <summary>
/// Los tooltips de la ventana principal que nombran un atajo, con la combinación que eligió el jugador. El XAML los
/// toma de <see cref="Instance"/> y se refrescan al cambiar un atajo.
/// </summary>
public sealed class HotkeyTips : ObservableObject
{
    public static HotkeyTips Instance { get; } = new();

    private HotkeyMap? _attached;

    /// <summary>Sigue los cambios del mapa con que corre la app (<see cref="HotkeyMap.Current"/>).</summary>
    public void Attach(HotkeyMap map)
    {
        if (_attached is not null)
            _attached.Changed -= Refresh;
        _attached = map;
        map.Changed += Refresh;
        Refresh();
    }

    private void Refresh() => OnPropertyChanged(string.Empty);

    private static string Key(string id) => HotkeyMap.Current.Text(id);

    public string StartStop => $"Sigue la BO (la sugerida en Automática, o la elegida de tu lista) con el overlay sobre el juego. También con {Key("start_stop")} desde el juego";
    public string Calibrate => $"También con {Key("calibrate")} desde el juego";
    public string Record => $"También con {Key("record")} desde el juego";
    public string CursorReminders => $"Junto al mouse: una casa cuando quedan 3 lugares de población y un centro urbano cuando está parado (debajo de Máx. aldeanos). También con {Key("cursor_reminders")} desde el juego";
    public string AutoStart => $"El coach arranca solo cuando se abre el juego, con la última BO usada (en la pantalla de carga cambia a la sugerida si encaja mejor; {Key("accept_suggestion")} vuelve), y se detiene con el informe cuando el juego se cierra. Si lo detenés a mano, no vuelve a arrancar hasta que abras el juego de nuevo";
    public string Diagnostic => $"Si algo no se lee bien: a los 2 s guarda la pantalla entera sin pérdida y un .txt con lo que encontró la app en %LOCALAPPDATA%\\BoCoach\\diagnostico, para mandar esa carpeta. También con {Key("diagnostic")} desde el juego";
    public string Back => $"Deshace lo último que se completó o salteó ({Key("back")} desde el juego)";
    public string Advance => $"Da por hecho el paso actual, por ejemplo si el coach no vio que lo terminaste ({Key("advance")} desde el juego)";
    public string Skip => $"Saltea el paso actual: no lo vas a hacer ({Key("skip")} desde el juego)";
}
