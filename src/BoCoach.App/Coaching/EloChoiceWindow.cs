using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BoCoach.Core.Web;

namespace BoCoach.App.Coaching;

/// <summary>
/// D10: elegir entre los jugadores con tu nombre que devolvió la API del juego (hasta 10), ordenados por la última
/// partida. Por renglón: nombre, país, Elo de 1v1 y de equipos, y cuándo jugó por última vez.
/// </summary>
public sealed class EloChoiceWindow : Window
{
    public EloCandidate? Selected { get; private set; }

    public EloChoiceWindow(IReadOnlyList<EloCandidate> candidates)
    {
        Title = "¿Cuál sos vos?";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x16));

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Hay varios jugadores con ese nombre. Elegí el tuyo (arriba, el que jugó más hace poco):",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF2, 0xEE)),
            Margin = new Thickness(0, 0, 0, 10),
        });
        var list = new ListBox { MaxHeight = 320, ItemsSource = candidates.Select(Line).ToList(), SelectedIndex = 0 };
        list.MouseDoubleClick += (_, _) => Accept(candidates, list.SelectedIndex);
        panel.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var use = new Button { Content = "Usar este", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        use.Click += (_, _) => Accept(candidates, list.SelectedIndex);
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
        buttons.Children.Add(use);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => Keyboard.Focus(list);
    }

    private void Accept(IReadOnlyList<EloCandidate> candidates, int index)
    {
        if (index < 0 || index >= candidates.Count)
            return;
        Selected = candidates[index];
        DialogResult = true;
    }

    private static string Line(EloCandidate c)
    {
        static string Elo(int? elo) => elo?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var country = string.IsNullOrWhiteSpace(c.Country) ? "" : $" ({c.Country.ToUpperInvariant()})";
        var last = c.LastMatch is { } date ? $" · última partida {date.ToLocalTime():yyyy-MM-dd}" : " · sin partidas clasificatorias";
        return $"{c.Alias}{country} · 1v1 {Elo(c.Elo1v1)} · equipos {Elo(c.EloTeam)}{last}";
    }
}
