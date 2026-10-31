using System.Windows.Input;
using BoCoach.App.Hotkeys;

namespace BoCoach.App.Tests;

/// <summary>Atajos editables (spec 2026-10-30, app para el público, parte 1).</summary>
public sealed class HotkeysTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Right", "Ctrl+Alt+Right", "Ctrl+Alt+→")]
    [InlineData("alt+ctrl+s", "Ctrl+Alt+S", "Ctrl+Alt+S")]
    [InlineData("Win+Shift+F5", "Shift+Win+F5", "Shift+Win+F5")]
    public void Lee_y_escribe_la_combinacion(string text, string storage, string display)
    {
        var binding = HotkeyBinding.Parse(text)!.Value;
        Assert.Equal(storage, binding.Storage);
        Assert.Equal(display, binding.Display);
        Assert.Equal(binding, HotkeyBinding.Parse(binding.Storage));
    }

    [Theory]
    [InlineData("S")]            // sin modificador: le robaría la tecla al juego
    [InlineData("Shift+S")]      // Shift solo tampoco alcanza
    [InlineData("Ctrl+Alt")]     // sin tecla
    [InlineData("Ctrl+Alt+Nada")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_lo_que_no_sirve(string? text) => Assert.Null(HotkeyBinding.Parse(text));

    [Fact]
    public void La_tecla_sola_de_un_modificador_no_vale() =>
        Assert.False(new HotkeyBinding(ModifierKeys.Control, Key.LeftAlt).IsValid);
}
