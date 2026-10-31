using System.Windows.Input;

namespace BoCoach.App.Hotkeys;

/// <summary>Una acción con atajo global: id para settings.json, nombre para la ventana "Atajos" y combinación de fábrica.</summary>
public sealed record HotkeyAction(string Id, string Name, HotkeyBinding Default, bool Developer);

/// <summary>Los atajos de la app (spec 2026-10-30, parte 1), en el orden de la ventana.</summary>
public static class HotkeyActions
{
    private const ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt;

    public static IReadOnlyList<HotkeyAction> All { get; } =
    [
        new("start_stop", "Empezar o detener el coach", new(CtrlAlt, Key.S), false),
        new("advance", "Ya está", new(CtrlAlt, Key.Right), false),
        new("back", "Atrás", new(CtrlAlt, Key.Left), false),
        new("skip", "Saltear", new(CtrlAlt, Key.Down), false),
        new("pause", "Pausa", new(CtrlAlt, Key.P), false),
        new("overlay", "Ocultar el overlay", new(CtrlAlt, Key.H), false),
        new("cursor_reminders", "Recordatorios en el mouse", new(CtrlAlt, Key.M), false),
        new("accept_suggestion", "Usar la BO sugerida", new(CtrlAlt, Key.B), false),
        new("calibrate", "Calibrar a mano", new(CtrlAlt, Key.C), true),
        new("record", "Grabar", new(CtrlAlt, Key.R), true),
        new("debug", "Debug del overlay", new(CtrlAlt, Key.D), true),
        new("diagnostic", "Captura de diagnóstico", new(CtrlAlt, Key.X), true),
    ];

    public static HotkeyAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
