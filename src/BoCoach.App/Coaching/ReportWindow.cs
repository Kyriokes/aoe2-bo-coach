using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BoCoach.App.Coaching;

/// <summary>
/// D5: el resumen de la partida que terminó (un renglón por tema) y un botón para abrir el informe completo, que queda
/// guardado en %LOCALAPPDATA%\BoCoach\informes.
/// </summary>
public sealed class ReportWindow : Window
{
    public ReportWindow(IReadOnlyList<string> summary, Action openFull)
    {
        Title = "Informe de la partida";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x16));

        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var (line, i) in summary.Select((l, i) => (l, i)))
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(i == 0 ? Color.FromRgb(0xE8, 0xC2, 0x6A) : Color.FromRgb(0xF4, 0xF2, 0xEE)),
                FontSize = i == 0 ? 16 : 13,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var open = new Button { Content = "Abrir el informe completo", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => openFull();
        var close = new Button { Content = "Cerrar", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
