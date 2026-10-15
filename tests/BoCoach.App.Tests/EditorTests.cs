using BoCoach.App;
using BoCoach.App.Editor;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.App.Tests;

public sealed class EditorTests : IDisposable
{
    private readonly DirectoryInfo _libraryDir = Directory.CreateTempSubdirectory("bocoach-editor-");
    private readonly CoachData _data;

    public EditorTests()
    {
        _data = CoachData.Load(_libraryDir.FullName);
    }

    public void Dispose() => _libraryDir.Delete(recursive: true);

    private BuildOrderEditorViewModel NewEditor() => new(new BuildOrder { Id = "x", Name = "Nueva" }, _data);

    private sealed class FakeDialogs : IDialogs
    {
        public bool SaveOnLeave { get; set; }
        public bool ConfirmAnswer { get; set; } = true;
        public string? FileToOpen { get; set; }
        public string? FileToSave { get; set; }
        public int SaveQuestions { get; private set; }
        public List<string> Errors { get; } = [];

        public string? PickFileToOpen() => FileToOpen;
        public string? PickFileToSave(string suggestedName) => FileToSave;
        public bool AskSaveChanges(string name) { SaveQuestions++; return SaveOnLeave; }
        public bool Confirm(string message) => ConfirmAnswer;
        public void ShowError(string message) => Errors.Add(message);
    }

    /// <summary>
    /// Versión automática del criterio de la Fase 4: armar una BO de 20 pasos desde cero solo con los
    /// botones de agregar y los campos del panel del paso.
    /// </summary>
    [Fact]
    public void Armar_una_BO_de_20_pasos_desde_cero()
    {
        var editor = NewEditor();
        editor.Name = "Scouts armada a mano";

        void Villagers(int count, GatherSource assign)
        {
            editor.AddVillagersCommand.Execute(null);
            editor.SelectedStep!.Count = count;
            editor.SelectedStep.Assign = assign;
        }

        void Build(string what, GatherSource? from, GatherSource assign, int count = 1, int? builders = null)
        {
            editor.AddBuildCommand.Execute(null);
            var step = editor.SelectedStep!;
            step.What = what;
            step.Count = count;
            step.From = from;
            step.Assign = assign;
            step.Builders = builders;
        }

        Build("house", null, GatherSource.Sheep, count: 2, builders: 3);
        Villagers(6, GatherSource.Sheep);
        Villagers(4, GatherSource.Wood);
        Build("lumber_camp", GatherSource.Wood, GatherSource.Wood);
        Build("house", GatherSource.Wood, GatherSource.Wood);
        Villagers(1, GatherSource.Hunt);
        Villagers(3, GatherSource.Berries);
        Build("mill", GatherSource.Berries, GatherSource.Berries);
        Villagers(2, GatherSource.Hunt);
        Build("house", GatherSource.Wood, GatherSource.Wood);
        Villagers(2, GatherSource.Wood);
        editor.AddResearchCommand.Execute(null);                // Telar
        editor.AddResearchCommand.Execute(null);                // Edad Feudal
        editor.SelectedStep!.AtPop = 22;
        Build("barracks", GatherSource.Wood, GatherSource.Wood);
        Build("stable", GatherSource.Wood, GatherSource.Wood);
        editor.SelectedStep!.AfterText = editor.Steps[12].Id;
        Build("farm", GatherSource.Sheep, GatherSource.Farm, count: 3);
        editor.AddMoveCommand.Execute(null);
        editor.SelectedStep!.From = GatherSource.Berries;
        editor.SelectedStep.Assign = GatherSource.Wood;
        Build("house", GatherSource.Wood, GatherSource.Wood);
        editor.AddVillagersCommand.Execute(null);
        editor.SelectedStep!.Action = StepAction.Train;
        editor.SelectedStep.What = "scout_cavalry";
        editor.SelectedStep.Count = 3;
        editor.AddResearchCommand.Execute(null);
        editor.SelectedStep!.What = "bloodlines";
        editor.FeudalTargetText = "9:00";

        Assert.Equal(20, editor.Steps.Count);
        Assert.Equal(Enumerable.Range(1, 20), editor.Steps.Select(s => s.Number));
        Assert.Equal(20, editor.Steps.Select(s => s.Id).Distinct().Count());
        Assert.False(editor.HasErrors, string.Join("\n", editor.Problems));
        Assert.Contains(editor.Summary, line => line.StartsWith("Feudal: click estimado"));
        Assert.All(editor.Steps, s => Assert.NotEqual("", s.EstimateText));
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void La_vista_previa_marca_el_bloqueo_de_poblacion()
    {
        var editor = NewEditor();
        editor.AddBuildCommand.Execute(null);
        editor.SelectedStep!.Count = 2;
        editor.SelectedStep.From = null;
        editor.SelectedStep.Builders = 3;
        editor.SelectedStep.Assign = GatherSource.Sheep;
        editor.AddVillagersCommand.Execute(null);
        editor.SelectedStep!.Count = 14;

        var blocked = editor.Steps[1];
        Assert.True(blocked.IsPopBlocked);
        Assert.Contains("Sin población", blocked.BlockText);
        Assert.Contains(editor.Summary, line => line.StartsWith("Bloqueo de población en el paso 2"));

        // Con una casa más antes de los aldeanos, el bloqueo desaparece.
        editor.SelectedStep = editor.Steps[0];
        editor.AddBuildCommand.Execute(null);
        editor.SelectedStep!.Count = 2;
        Assert.False(editor.Steps[2].IsPopBlocked);
        Assert.DoesNotContain(editor.Summary, line => line.StartsWith("Bloqueo de población"));
    }

    [Fact]
    public void Con_errores_no_simula_y_los_muestra_por_paso()
    {
        var editor = NewEditor();
        editor.AddVillagersCommand.Execute(null);

        editor.SelectedStep!.Assign = null;

        Assert.True(editor.HasErrors);
        Assert.True(editor.SelectedStep.HasError);
        Assert.Contains("assign", editor.SelectedStep.IssueText);
        Assert.Equal(["Corregí los errores para ver la vista previa."], editor.Summary);
        Assert.Contains(editor.Problems, p => p.StartsWith("Error (paso 1)"));
    }

    [Fact]
    public void Cambiar_la_accion_elige_una_entidad_valida()
    {
        var editor = NewEditor();
        editor.AddVillagersCommand.Execute(null);

        editor.SelectedStep!.Action = StepAction.Build;

        Assert.Equal(EntityKind.Building, _data.GameData.Get(editor.SelectedStep.What!).Kind);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Problems));
    }

    [Fact]
    public void Reordenar_mueve_y_renumera()
    {
        var editor = new BuildOrderEditorViewModel(
            BuildOrderSerializer.Load(Path.Combine(CoachData.DataDir, "build_orders", "scouts_feudal.json")), _data);
        var last = editor.Steps[^1];

        editor.MoveStep(editor.Steps.Count - 1, 0);

        Assert.Same(last, editor.Steps[0]);
        Assert.Equal(1, last.Number);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Insertar_va_despues_del_paso_elegido_y_lo_selecciona()
    {
        var editor = NewEditor();
        editor.AddVillagersCommand.Execute(null);
        editor.AddVillagersCommand.Execute(null);
        editor.SelectedStep = editor.Steps[0];

        editor.AddMoveCommand.Execute(null);

        Assert.Equal(StepAction.Move, editor.Steps[1].Action);
        Assert.Same(editor.Steps[1], editor.SelectedStep);
        Assert.Equal("s3", editor.Steps[1].Id);
    }

    [Fact]
    public void Guardar_duplicar_y_mandar_a_la_papelera()
    {
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(_data, dialogs);
        Assert.Equal(3, main.Entries.Count); // ejemplos copiados en el primer uso

        main.NewCommand.Execute(null);
        main.Editor!.Name = "Mi BO";
        main.Editor.AddVillagersCommand.Execute(null);
        main.SaveCommand.Execute(null);

        Assert.Equal(4, main.Entries.Count);
        Assert.Equal("Mi BO", main.SelectedEntry?.Name);
        Assert.False(main.Editor.IsDirty);

        main.DuplicateCommand.Execute(null);
        Assert.Equal("Mi BO (copia)", main.SelectedEntry?.Name);
        Assert.Equal(5, main.Entries.Count);

        main.TrashCommand.Execute(null);
        Assert.Equal(4, main.Entries.Count);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_libraryDir.FullName, BuildOrderLibrary.TrashFolder)).Any());
    }

    [Fact]
    public void Al_cambiar_de_BO_pregunta_si_guardar_los_cambios()
    {
        var dialogs = new FakeDialogs { SaveOnLeave = true };
        var main = new MainViewModel(_data, dialogs);
        var first = main.SelectedEntry!;
        main.Editor!.Name = "Renombrada";

        main.SelectedEntry = main.Entries.First(e => e.Path != first.Path);

        Assert.Equal(1, dialogs.SaveQuestions);
        Assert.Contains(main.Entries, e => e.Name == "Renombrada");
        Assert.NotEqual(first.Path, main.SelectedEntry?.Path);
    }

    [Fact]
    public void Buscar_filtra_la_biblioteca()
    {
        var main = new MainViewModel(_data, new FakeDialogs());

        main.SearchText = "castle";

        Assert.Equal("Fast Castle (27 + 2)", Assert.Single(main.Entries).Name);
    }

    [Fact]
    public void Importar_un_archivo_roto_muestra_el_error()
    {
        var broken = Path.Combine(Path.GetTempPath(), $"rota-{Guid.NewGuid():N}.json");
        File.WriteAllText(broken, "{ nada");
        var dialogs = new FakeDialogs { FileToOpen = broken };
        var main = new MainViewModel(_data, dialogs);
        try
        {
            main.ImportCommand.Execute(null);

            Assert.Contains("JSON inválido", Assert.Single(dialogs.Errors));
        }
        finally
        {
            File.Delete(broken);
        }
    }
}
