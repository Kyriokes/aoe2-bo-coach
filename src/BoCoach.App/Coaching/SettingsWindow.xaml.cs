using System.Windows;
using System.Windows.Controls;

namespace BoCoach.App.Coaching;

/// <summary>
/// Los ajustes, fuera de la vista principal (spec 2026-11-02): Partida, Juego y HUD, Avanzado y, con el modo
/// desarrollador, las herramientas. No es modal: se puede dejar abierta mientras se juega.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(HomeViewModel viewModel, int tab = 0)
    {
        InitializeComponent();
        DataContext = viewModel;
        Tabs.SelectedIndex = tab;
        // Los números y el nombre se aplican al salir de la caja: cerrar la ventana con el foco adentro no lo dispara.
        Closing += (_, _) =>
        {
            foreach (var box in new[] { NameBox, EloSoloBox, EloTeamBox, MaxVillagersBox })
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
