using System.Windows;

namespace BoCoach.App.Coaching;

/// <summary>
/// D5: el resumen de la partida que terminó (un renglón por tema) y un botón para abrir el informe completo, que queda
/// guardado en %LOCALAPPDATA%\BoCoach\informes.
/// </summary>
public partial class ReportWindow : Window
{
    private readonly Action _openFull;
    private readonly Func<string, Task<bool>>? _sendNote;

    /// <param name="notePrompt">La pregunta de la nota; sin ella (no se comparten las partidas) no se muestra.</param>
    /// <param name="sendNote">Guarda la nota para que salga con los registros. True si quedó guardada.</param>
    public ReportWindow(IReadOnlyList<string> summary, Action openFull, string? notePrompt = null, Func<string, Task<bool>>? sendNote = null)
    {
        InitializeComponent();
        _openFull = openFull;
        Heading.Text = summary.Count > 0 ? summary[0] : Texts.ReportTitle;
        Lines.ItemsSource = summary.Skip(1).ToList();
        // Un informe con muchos renglones no se sale de la pantalla: se desplaza adentro de la tarjeta.
        LinesScroll.MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - (notePrompt is null ? 220 : 400));
        _sendNote = sendNote;
        NoteText.MaxLength = Core.Record.GameRecordNote.MaxLength;
        if (notePrompt is not null && sendNote is not null)
        {
            NotePrompt.Text = notePrompt;
            NotePanel.Visibility = Visibility.Visible;
        }
    }

    private async void SendNote_Click(object sender, RoutedEventArgs e)
    {
        if (_sendNote is null || string.IsNullOrWhiteSpace(NoteText.Text))
            return;
        NoteSend.IsEnabled = false;
        var saved = await _sendNote(NoteText.Text);
        NoteStatus.Text = saved ? Texts.NoteThanks : Texts.NoteFailed;
        // Una nota guardada ya no se edita: otra saldría como otra nota. Si falló, se puede volver a probar.
        NoteText.IsReadOnly = saved;
        NoteSend.IsEnabled = !saved;
    }

    private void Open_Click(object sender, RoutedEventArgs e) => _openFull();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
