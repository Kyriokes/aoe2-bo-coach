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
        new("start_stop", Texts.HotkeyStartStop, new(CtrlAlt, Key.S), false),
        new("advance", Texts.HotkeyAdvance, new(CtrlAlt, Key.Right), false),
        new("back", Texts.HotkeyBack, new(CtrlAlt, Key.Left), false),
        new("skip", Texts.HotkeySkip, new(CtrlAlt, Key.Down), false),
        new("pause", Texts.HotkeyPause, new(CtrlAlt, Key.P), false),
        new("overlay", Texts.HotkeyOverlay, new(CtrlAlt, Key.H), false),
        new("cursor_reminders", Texts.HotkeyCursorReminders, new(CtrlAlt, Key.M), false),
        new("accept_suggestion", Texts.HotkeyAcceptSuggestion, new(CtrlAlt, Key.B), false),
        new("calibrate", Texts.HotkeyCalibrate, new(CtrlAlt, Key.C), true),
        new("record", Texts.HotkeyRecord, new(CtrlAlt, Key.R), true),
        new("debug", Texts.HotkeyDebug, new(CtrlAlt, Key.D), true),
        new("diagnostic", Texts.HotkeyDiagnostic, new(CtrlAlt, Key.X), true),
    ];

    public static HotkeyAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
