using System.Windows;
using System.Windows.Controls;

namespace BoCoach.App;

/// <summary>Un diálogo chico que pide un texto (por ejemplo, el código de una BO de la web).</summary>
public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 14), MinWidth = 380 };
        var ok = new Button { Content = "Aceptar", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0) };
        var window = new Window
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("Background"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                MaxWidth = 460,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
