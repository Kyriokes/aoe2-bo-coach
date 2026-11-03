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

    public string StartStop => Texts.TipStartStop(Key("start_stop"));
    public string Calibrate => Texts.TipFromGame(Key("calibrate"));
    public string Record => Texts.TipFromGame(Key("record"));
    public string CursorReminders => Texts.TipCursorReminders(Key("cursor_reminders"));
    public string AutoStart => Texts.TipAutoStart(Key("accept_suggestion"));
    public string Diagnostic => Texts.TipDiagnostic(Key("diagnostic"));
    public string Back => Texts.TipBack(Key("back"));
    public string Advance => Texts.TipAdvance(Key("advance"));
    public string Skip => Texts.TipSkip(Key("skip"));
}
