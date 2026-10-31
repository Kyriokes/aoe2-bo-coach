using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Editor;

/// <summary>Diálogos que necesita la ventana del editor (los implementa la vista).</summary>
public interface IDialogs
{
    string? PickFileToOpen();
    string? PickFileToSave(string suggestedName);

    /// <summary>True = guardar, false = descartar.</summary>
    bool AskSaveChanges(string name);

    bool Confirm(string message);
    void ShowError(string message);

    /// <summary>Pide un texto (por ejemplo, el código de una BO de la web). null = cancelado.</summary>
    string? AskText(string title, string message, string initial = "");

    /// <summary>Muestra el diálogo "Cuenta de la web". null = cancelado.</summary>
    WebSettings? EditWebAccount(WebSettings current);

    /// <summary>Abre una dirección de la web en el navegador.</summary>
    void OpenUrl(string url);

    /// <summary>El texto copiado en el portapapeles, o null si no hay.</summary>
    string? ClipboardText() => null;
}

/// <summary>Biblioteca local a la izquierda y el editor de la BO elegida.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    // Una BO de la web pesa unos 20 KB: una respuesta de más de 8 MB no es una BO y no se carga entera en memoria.
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };

    private readonly CoachData _app;
    private readonly IDialogs _dialogs;
    private readonly WebAccount _web;
    private bool _suppressOpen;

    /// <param name="web">La cuenta de la biblioteca web (por defecto, la de %APPDATA%\BoCoach\web.json).</param>
    /// <summary>La sección "Apoyar el proyecto" de la web (Cafecito, PayPal, Ko-fi, Mercado Pago y las metas).</summary>
    public string SupportUrl => new Uri(_web.Settings.BaseUri, "#apoyar").ToString();

    public MainViewModel(CoachData app, IDialogs dialogs, WebAccount? web = null)
    {
        _app = app;
        _dialogs = dialogs;
        _web = web ?? new WebAccount(SharedHttp);
        RefreshList();
        SelectedEntry = Entries.FirstOrDefault(e => e.BuildOrder is not null);
    }

    public ObservableCollection<LibraryEntry> Entries { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TrashCommand))]
    public partial LibraryEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DuplicateCommand), nameof(ExportCommand), nameof(PublishToWebCommand))]
    public partial BuildOrderEditorViewModel? Editor { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

    public string LibraryPath => _app.Library.Directory;

    partial void OnSearchTextChanged(string value) => RefreshList();

    public const string AllCivs = "(todas)";
    public const string GameCivFilter = "(la de la partida)";

    /// <summary>
    /// D7: con una BO por civ la lista es larga. Filtra por civ: todas, la de la partida (la de la barra de arriba) o
    /// una civ. Las BO para cualquier civ ("any") se ven siempre.
    /// </summary>
    [ObservableProperty] public partial string CivFilter { get; set; } = AllCivs;

    partial void OnCivFilterChanged(string value) => RefreshList();

    public IReadOnlyList<string> CivFilterOptions => [AllCivs, GameCivFilter, .. _app.GameData.Civs.Keys.Order()];

    /// <summary>La civ de la partida (null = la de la BO): la da la ventana con lo elegido en la barra de arriba.</summary>
    public Func<string?> GameCiv { get; set; } = () => null;

    /// <summary>La civ de la partida cambió: si el filtro la usa, la lista se rehace.</summary>
    public void GameCivChanged()
    {
        if (CivFilter == GameCivFilter)
            RefreshList();
    }

    private bool PassesCivFilter(LibraryEntry entry)
    {
        var civ = CivFilter == GameCivFilter ? GameCiv() : CivFilter == AllCivs ? null : CivFilter;
        return civ is null || entry.BuildOrder is not { } bo
            || string.Equals(bo.Civ, civ, StringComparison.OrdinalIgnoreCase) || string.Equals(bo.Civ, "any", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedEntryChanged(LibraryEntry? value)
    {
        if (_suppressOpen || value is null)
            return;
        if (value.BuildOrder is null)
        {
            Status = $"No se puede abrir {Path.GetFileName(value.Path)}: {value.Error}";
            return;
        }
        LeaveCurrent();
        Editor = new BuildOrderEditorViewModel(value.BuildOrder, _app);
        Status = $"Abierta: {value.Name}";
    }

    [RelayCommand]
    private void New()
    {
        LeaveCurrent();
        Select(null);
        Editor = new BuildOrderEditorViewModel(
            new BuildOrder { Id = BuildOrderLibrary.NewId(), Name = "Nueva build order" }, _app);
        Status = "BO nueva: agregá pasos con los botones de arriba (o Ctrl+1 a Ctrl+4).";
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void Save()
    {
        // D12 (f): una BO asistida con los pasos cambiados pasa a manual (ToBuildOrder ya no lleva sus objetivos).
        var dropped = Editor?.AssistedDropped == true ? "Esta BO era asistida: pasa a manual. " : "";
        if (SaveEditor() is not { } saved)
            return;
        RefreshList();
        Select(Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(saved)));
        Status = dropped + (Editor!.HasErrors
            ? $"Guardada \"{saved.Name}\", pero todavía tiene errores."
            : $"Guardada \"{saved.Name}\".");
    }

    private BuildOrder? SaveEditor()
    {
        if (Editor is not { } editor)
            return null;
        var saved = _app.Library.Save(editor.ToBuildOrder());
        editor.MarkSaved(saved);
        return saved;
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void Duplicate()
    {
        if (Editor is not { } editor)
            return;
        LeaveCurrent();
        var copy = _app.Library.Duplicate(editor.ToBuildOrder());
        RefreshList();
        SelectedEntry = Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(copy));
        Status = $"Duplicada como \"{copy.Name}\".";
    }

    /// <summary>Importa un archivo: una BO nuestra o una exportada de buildorderguide.com (formato de RTS Overlay).</summary>
    [RelayCommand]
    private void Import()
    {
        if (_dialogs.PickFileToOpen() is not { } path)
            return;
        try
        {
            AddImported(File.ReadAllText(path));
        }
        catch (Exception e) when (e is BuildOrderFormatException or IOException)
        {
            _dialogs.ShowError($"No se pudo importar el archivo.\n\n{e.Message}");
        }
    }

    /// <summary>Importa la BO copiada (por ejemplo, el JSON que da "Exportar" en buildorderguide.com).</summary>
    [RelayCommand]
    private void Paste()
    {
        if (_dialogs.ClipboardText() is not { Length: > 0 } text)
        {
            _dialogs.ShowError("No hay nada copiado. En buildorderguide.com, exportá la BO, copiá el texto y volvé a tocar \"Pegar BO\".");
            return;
        }
        try
        {
            AddImported(text);
        }
        catch (BuildOrderFormatException e)
        {
            _dialogs.ShowError($"Lo copiado no es una build order.\n\n{e.Message}");
        }
    }

    private void AddImported(string json)
    {
        var (bo, warnings, _) = BuildOrderImport.Read(json, _app.GameData);
        LeaveCurrent();
        var imported = _app.Library.Add(bo);
        RefreshList();
        SelectedEntry = Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(imported));
        Status = warnings.Count == 0
            ? $"Importada \"{imported.Name}\"."
            : $"Importada \"{imported.Name}\", revisala: {string.Join(" · ", warnings.Take(3))}{(warnings.Count > 3 ? $" (y {warnings.Count - 3} más)" : "")}";
    }

    /// <summary>
    /// Publica la BO abierta en la biblioteca web (la guarda antes). Si ya se había publicado desde esta PC, la
    /// reemplaza y mantiene su código y sus votos.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasEditor))]
    private async Task PublishToWebAsync()
    {
        if (Editor is not { } editor)
            return;
        if (editor.HasErrors)
        {
            _dialogs.ShowError("Corregí los errores de la BO antes de publicarla.");
            return;
        }
        if (!_web.Settings.HasToken)
        {
            if (_dialogs.EditWebAccount(_web.Settings) is not { HasToken: true } settings)
                return;
            _web.Save(settings);
        }

        var saved = SaveEditor()!;
        RefreshList();
        Status = "Publicando en la web…";
        try
        {
            var published = await _web.PublishAsync(saved, _app.Planner);
            Status = $"\"{saved.Name}\" publicada en la web con el código {published.Code}.";
            if (_dialogs.Confirm($"\"{saved.Name}\" quedó publicada con el código {published.Code}.\n\n¿Abrir su página en el navegador?"))
                _dialogs.OpenUrl(published.Url);
        }
        catch (WebLibraryException e)
        {
            Status = "";
            _dialogs.ShowError($"No se pudo publicar.\n\n{e.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportFromWebAsync()
    {
        var text = _dialogs.AskText("Importar desde la web",
            "Pegá el código de la BO (6 letras y números), el enlace «Abrir en BO Coach» o la dirección de su página:");
        if (!string.IsNullOrWhiteSpace(text))
            await ImportFromLinkAsync(text);
    }

    /// <summary>
    /// Baja una BO de la web y la agrega a la biblioteca. Con <paramref name="confirm"/> pregunta antes de agregarla,
    /// mostrando nombre y autor: es para los enlaces bocoach://import/..., que los dispara cualquier página web.
    /// </summary>
    public async Task<bool> ImportFromLinkAsync(string text, bool confirm = false)
    {
        if (WebCode.Parse(text) is not { } code)
        {
            _dialogs.ShowError($"«{text.Trim()}» no tiene un código de la biblioteca web (son 6 letras y números).");
            return false;
        }
        Status = $"Bajando {code} de la web…";
        try
        {
            var downloaded = await _web.Client.DownloadAsync(code);
            if (confirm && !_dialogs.Confirm(
                    $"Una página web o un programa pidió abrir una build order en BO Coach.\n\n¿Agregar \"{downloaded.BuildOrder.Name}\" de {downloaded.Author} (código {code}) a tu biblioteca?"))
            {
                Status = "";
                return false;
            }
            LeaveCurrent();
            var imported = _app.Library.Add(downloaded.BuildOrder);
            RefreshList();
            SelectedEntry = Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(imported));
            Status = $"Importada \"{imported.Name}\" de {downloaded.Author} (código {code}).";
            return true;
        }
        catch (WebLibraryException e)
        {
            Status = "";
            _dialogs.ShowError($"No se pudo importar {code}.\n\n{e.Message}");
            return false;
        }
    }

    [RelayCommand]
    private void WebAccountSettings()
    {
        if (_dialogs.EditWebAccount(_web.Settings) is not { } settings)
            return;
        _web.Save(settings);
        Status = settings.HasToken
            ? "Cuenta de la web guardada."
            : "Cuenta de la web guardada sin token: se puede importar, pero para publicar hace falta el token.";
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void Export()
    {
        if (Editor is not { } editor)
            return;
        var bo = editor.ToBuildOrder();
        if (_dialogs.PickFileToSave($"{bo.Name}.json") is not { } path)
            return;
        BuildOrderLibrary.Export(bo, path);
        Status = $"Exportada a {path}";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void Trash()
    {
        if (SelectedEntry is not { } entry)
            return;
        if (!_dialogs.Confirm($"¿Mandar \"{entry.Name}\" a la papelera de la biblioteca?\n\nNo se borra: queda en {Path.Combine(LibraryPath, BuildOrderLibrary.TrashFolder)}."))
            return;
        var moved = _app.Library.MoveToTrash(entry.Path);
        Editor = null;
        RefreshList();
        Select(null);
        Status = $"Movida a la papelera: {moved}";
    }

    /// <summary>Si la BO abierta tiene cambios sin guardar, pregunta si guardarlos.</summary>
    public void LeaveCurrent()
    {
        if (Editor is { IsDirty: true } editor && _dialogs.AskSaveChanges(editor.Name))
        {
            SaveEditor();
            RefreshList(); // mantiene la selección actual
        }
    }

    /// <summary>Vuelve a leer la carpeta de la biblioteca (por si cambió desde afuera).</summary>
    public void Reload() => RefreshList();

    private bool HasEditor() => Editor is not null;

    private bool HasSelectedEntry() => SelectedEntry is not null;

    private void RefreshList()
    {
        var selectedPath = SelectedEntry?.Path;
        _suppressOpen = true;
        Entries.Clear();
        foreach (var entry in _app.Library.Search(SearchText).Where(PassesCivFilter))
            Entries.Add(entry);
        SelectedEntry = Entries.FirstOrDefault(e => e.Path == selectedPath);
        _suppressOpen = false;
    }

    private void Select(LibraryEntry? entry)
    {
        _suppressOpen = true;
        SelectedEntry = entry;
        _suppressOpen = false;
    }
}
