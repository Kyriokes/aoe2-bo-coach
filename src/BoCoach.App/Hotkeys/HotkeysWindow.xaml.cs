using System.Windows;
using System.Windows.Input;

namespace BoCoach.App.Hotkeys;

/// <summary>Ver y cambiar los atajos. No es modal; mientras un renglón espera la combinación, las teclas van a él.</summary>
public partial class HotkeysWindow : Window
{
    private readonly HotkeysViewModel _viewModel;

    public HotkeysWindow(HotkeysViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => viewModel.CancelCapture();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.Rows.FirstOrDefault(r => r.Capturing) is not { } row)
            return;
        // Con Alt apretado WPF manda Key.System y la tecla de verdad en SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _viewModel.Capture(row.Id, Keyboard.Modifiers, key);
        e.Handled = true;
    }
}
