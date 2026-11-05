using System.Windows;
using System.Windows.Controls;

namespace BoCoach.App;

/// <summary>Un diálogo chico que pide un texto (por ejemplo, el código de una BO de la web).</summary>
public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string message, string initial = "")
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 14), MinWidth = 380 };
        var ok = new Button { Content = Texts.Accept, IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = Texts.Cancel, IsCancel = true, Margin = new Thickness(0) };
        var window = new Window
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Style = (Style)Application.Current.FindResource("AppWindow"),
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

/// <summary>Los mensajes y las confirmaciones de la app, con el tema (el MessageBox de Windows es claro).</summary>
public static class AppDialog
{
    /// <summary>Un mensaje con aceptar.</summary>
    public static void Show(Window owner, string title, string message) =>
        Open(owner, title, message, Texts.Accept, null);

    /// <summary>Una pregunta por sí o por no.</summary>
    /// <param name="noByDefault">Enter responde que no, y ese es el botón destacado.</param>
    public static bool Confirm(Window owner, string message, bool noByDefault = false) =>
        Open(owner, Texts.ConfirmTitle, message, Texts.Yes, Texts.No, noByDefault);

    private static bool Open(Window owner, string title, string message, string accept, string? cancel, bool noByDefault = false)
    {
        var primary = (Style)Application.Current.FindResource("PrimaryButton");
        var ok = new Button { Content = accept, Margin = new Thickness(0) };
        if (!noByDefault)
            (ok.IsDefault, ok.Style) = (true, primary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (cancel is null)
            ok.IsCancel = true;
        buttons.Children.Add(ok);
        if (cancel is not null)
        {
            ok.Margin = new Thickness(0, 0, 6, 0);
            var no = new Button { Content = cancel, IsCancel = true, Margin = new Thickness(0) };
            if (noByDefault)
                (no.IsDefault, no.Style) = (true, primary);
            buttons.Children.Add(no);
        }
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Style = (Style)Application.Current.FindResource("AppWindow"),
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons },
            },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }
}
