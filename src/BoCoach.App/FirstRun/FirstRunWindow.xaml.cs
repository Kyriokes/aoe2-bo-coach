using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BoCoach.App.FirstRun;

/// <summary>
/// Tutorial de primer uso (C4). No es modal: el jugador va al juego con la ventana abierta y la app mira la pantalla
/// cada 2 s mientras <see cref="FirstRunViewModel.Watching"/>.
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = FirstRunViewModel.WatchInterval };

    /// <param name="languageContext">Lo que tiene el idioma del juego (<c>LanguageOptions</c> y <c>Language</c>): el coach.</param>
    /// <param name="installMod">El botón "Instalar mod del HUD" de la barra.</param>
    public FirstRunWindow(FirstRunViewModel viewModel, object languageContext, ICommand installMod)
    {
        InitializeComponent();
        DataContext = viewModel;
        LanguageBox.DataContext = languageContext;
        InstallModButton.Command = installMod;
        _timer.Tick += async (_, _) => await viewModel.TickAsync();
        _timer.Start();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.Finished) && viewModel.Finished)
                Close();
        };
        Closed += (_, _) => _timer.Stop();
    }
}
