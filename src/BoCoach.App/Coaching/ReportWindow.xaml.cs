using System.Windows;

namespace BoCoach.App.Coaching;

/// <summary>
/// D5: el resumen de la partida que terminó (un renglón por tema) y un botón para abrir el informe completo, que queda
/// guardado en %LOCALAPPDATA%\BoCoach\informes.
/// </summary>
public partial class ReportWindow : Window
{
    private readonly Action _openFull;

    public ReportWindow(IReadOnlyList<string> summary, Action openFull)
    {
        InitializeComponent();
        _openFull = openFull;
        Heading.Text = summary.Count > 0 ? summary[0] : Texts.ReportTitle;
        Lines.ItemsSource = summary.Skip(1).ToList();
        // Un informe con muchos renglones no se sale de la pantalla: se desplaza adentro de la tarjeta.
        LinesScroll.MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 220);
    }

    private void Open_Click(object sender, RoutedEventArgs e) => _openFull();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
