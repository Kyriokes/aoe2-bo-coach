using System.Collections.ObjectModel;
using System.IO;
using BoCoach.Core.BuildOrders;
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
}

/// <summary>Biblioteca local a la izquierda y el editor de la BO elegida.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly CoachData _app;
    private readonly IDialogs _dialogs;
    private bool _suppressOpen;

    public MainViewModel(CoachData app, IDialogs dialogs)
    {
        _app = app;
        _dialogs = dialogs;
        RefreshList();
        SelectedEntry = Entries.FirstOrDefault(e => e.BuildOrder is not null);
    }

    public ObservableCollection<LibraryEntry> Entries { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TrashCommand))]
    public partial LibraryEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DuplicateCommand), nameof(ExportCommand))]
    public partial BuildOrderEditorViewModel? Editor { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";

    public string LibraryPath => _app.Library.Directory;

    partial void OnSearchTextChanged(string value) => RefreshList();

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
        if (SaveEditor() is not { } saved)
            return;
        RefreshList();
        Select(Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(saved)));
        Status = Editor!.HasErrors
            ? $"Guardada \"{saved.Name}\", pero todavía tiene errores."
            : $"Guardada \"{saved.Name}\".";
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

    [RelayCommand]
    private void Import()
    {
        if (_dialogs.PickFileToOpen() is not { } path)
            return;
        try
        {
            LeaveCurrent();
            var imported = _app.Library.Import(path);
            RefreshList();
            SelectedEntry = Entries.FirstOrDefault(e => e.Path == _app.Library.PathFor(imported));
            Status = $"Importada \"{imported.Name}\".";
        }
        catch (Exception e) when (e is BuildOrderFormatException or IOException)
        {
            _dialogs.ShowError($"No se pudo importar el archivo.\n\n{e.Message}");
        }
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
        foreach (var entry in _app.Library.Search(SearchText))
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
