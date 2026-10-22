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

    private static readonly BuildOrder ScoutsBo = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "build_orders", "scouts_feudal.json"));

    // Los aldeanos al llegar a Castillos (scouts_22p los trae) no se muestran en el editor, pero no se pierden al guardar.
    [Fact]
    public void Guardar_conserva_los_aldeanos_al_llegar_a_Castillos()
    {
        var bo = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "data", "build_orders", "scouts_22p.json"));
        Assert.NotNull(bo.Targets?.VillagersAtCastle);

        Assert.Equal(bo.Targets!.VillagersAtCastle, new BuildOrderEditorViewModel(bo, _data).ToBuildOrder().Targets?.VillagersAtCastle);
    }

    // D3: las ramas se arman en el editor eligiendo de qué se ven los pasos.
    [Fact]
    public void Una_rama_se_arma_eligiendo_su_senal_y_se_guarda_aparte_de_la_BO()
    {
        var vm = new BuildOrderEditorViewModel(ScoutsBo, _data);
        var mainCount = vm.Steps.Count;

        vm.SelectedBranch = "rival_feudal_click";
        Assert.True(vm.IsBranch);
        Assert.Empty(vm.Steps);
        vm.AddVillagersCommand.Execute(null);
        vm.BranchBeforeText = "10:00";
        vm.BranchNote = "Más aldeanos a la madera";

        var bo = vm.ToBuildOrder();
        Assert.Equal(mainCount, bo.Steps.Count);
        var branch = Assert.Single(bo.Branches!);
        Assert.Equal(("rival_feudal_click", 600, "Más aldeanos a la madera", 1), (branch.On, branch.Before, branch.Note, branch.Steps.Count));
        Assert.DoesNotContain(branch.Steps[0].Id, bo.Steps.Select(s => s.Id)); // ids distintos de los de la BO
        Assert.True(vm.IsDirty);

        vm.SelectedBranch = BuildOrderEditorViewModel.MainSteps;
        Assert.Equal(mainCount, vm.Steps.Count);
        Assert.Single(vm.ToBuildOrder().Branches!);
    }

    [Fact]
    public void Una_BO_con_ramas_las_muestra_al_elegir_su_senal()
    {
        var bo = ScoutsBo with { Branches = [new Branch { On = "under_attack", Note = "Torre", Steps = [new Step { Id = "a1", Do = StepAction.Build, What = "watch_tower" }] }] };
        var vm = new BuildOrderEditorViewModel(bo, _data);

        vm.SelectedBranch = "under_attack";
        Assert.Single(vm.Steps);
        Assert.Equal("Torre", vm.BranchNote);
        Assert.False(vm.IsDirty);
    }

    // D8: el mapa de la BO, con la lista de data/maps.json; "any" no se guarda.
    [Fact]
    public void El_mapa_se_elige_de_la_lista_y_se_guarda_solo_si_no_es_cualquiera()
    {
        var vm = new BuildOrderEditorViewModel(ScoutsBo, _data);

        Assert.Equal("any", vm.Map);
        Assert.Equal("Cualquiera", vm.MapOptions[0].Label);
        Assert.Contains(vm.MapOptions, o => o.Value == "arabia" && o.Label == "Arabia");
        Assert.Null(vm.ToBuildOrder().Map);

        vm.Map = "arabia";
        Assert.True(vm.IsDirty);
        Assert.Equal("arabia", vm.ToBuildOrder().Map);
        Assert.Equal("arabia", new BuildOrderEditorViewModel(vm.ToBuildOrder(), _data).Map);

        // Una BO con un mapa que no está en el archivo lo conserva, y la lista lo muestra para que se vea.
        var raro = new BuildOrderEditorViewModel(ScoutsBo with { Map = "atlantis" }, _data);
        Assert.Equal("atlantis", raro.Map);
        Assert.Contains(raro.MapOptions, o => o.Value == "atlantis");
        Assert.Contains(raro.Problems, p => p.Contains("atlantis"));
    }

    // D2: la postura de cierre. Sin tocarla, una BO que no la tenía se guarda sin postura (sigue a sus pasos).
    [Fact]
    public void La_postura_deducida_se_muestra_y_sin_tocarla_no_se_guarda()
    {
        var vm = new BuildOrderEditorViewModel(ScoutsBo, _data);

        Assert.True(vm.PostureVillagers);
        Assert.Equal("scout_cavalry", vm.PostureUnit);
        Assert.Equal("castle_age", vm.PostureNextAge);
        Assert.Null(vm.ToBuildOrder().Posture);
    }

    [Fact]
    public void Cambiar_la_postura_la_guarda()
    {
        var vm = new BuildOrderEditorViewModel(ScoutsBo, _data);
        vm.PostureUnit = "knight";

        var posture = vm.ToBuildOrder().Posture;
        Assert.True(vm.IsDirty);
        Assert.Equal(["villager", "knight"], posture!.Queue);
        Assert.Equal("castle_age", posture.NextAge);
        Assert.Contains(vm.PostureUnitOptions, o => o.Value == "knight" && o.Label == "caballero");
    }

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
        public string? AskText(string title, string message, string initial = "") => null;
        public BoCoach.App.Web.WebSettings? EditWebAccount(BoCoach.App.Web.WebSettings current) => null;
        public void OpenUrl(string url) { }
        public string? Clipboard { get; set; }
        public string? ClipboardText() => Clipboard;
    }

    [Fact]
    public void Pegar_una_BO_de_buildorderguide_la_convierte_y_la_agrega()
    {
        var dialogs = new FakeDialogs
        {
            Clipboard = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rts-overlay", "23p-archers.json")),
        };
        var vm = new MainViewModel(_data, dialogs);

        vm.PasteCommand.Execute(null);

        Assert.Empty(dialogs.Errors);
        Assert.Equal("23p Archers", vm.Editor?.Name);
        Assert.Contains(vm.Entries, e => e.BuildOrder?.Name == "23p Archers");
        Assert.Equal("Importada \"23p Archers\".", vm.Status);
    }

    [Fact]
    public void Pegar_sin_nada_copiado_avisa()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(_data, dialogs);

        vm.PasteCommand.Execute(null);

        Assert.Single(dialogs.Errors);
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
        Villagers(9, GatherSource.Sheep);   // totales en la fuente, como el HUD (formato 2)
        Villagers(4, GatherSource.Wood);
        Build("lumber_camp", GatherSource.Wood, GatherSource.Wood);
        Build("house", GatherSource.Wood, GatherSource.Wood);
        Villagers(1, GatherSource.Hunt);
        Villagers(3, GatherSource.Berries);
        Build("mill", GatherSource.Berries, GatherSource.Berries);
        Villagers(3, GatherSource.Hunt);
        Build("house", GatherSource.Wood, GatherSource.Wood);
        Villagers(6, GatherSource.Wood);
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
        editor.SelectedStep!.Count = 17; // 14 nuevos más los 3 iniciales en las ovejas

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
            BuildOrderSerializer.Load(Path.Combine(CoachData.DataDir, "build_orders", "scouts_22p.json")), _data);
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
        var examples = main.Entries.Count; // ejemplos copiados en el primer uso: los 3 de siempre y una por civ (D7)
        Assert.True(examples > 60, $"{examples} ejemplos");

        main.NewCommand.Execute(null);
        main.Editor!.Name = "Mi BO";
        main.Editor.AddVillagersCommand.Execute(null);
        main.SaveCommand.Execute(null);

        Assert.Equal(examples + 1, main.Entries.Count);
        Assert.Equal("Mi BO", main.SelectedEntry?.Name);
        Assert.False(main.Editor.IsDirty);

        main.DuplicateCommand.Execute(null);
        Assert.Equal("Mi BO (copia)", main.SelectedEntry?.Name);
        Assert.Equal(examples + 2, main.Entries.Count);

        main.TrashCommand.Execute(null);
        Assert.Equal(examples + 1, main.Entries.Count);
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

        main.SearchText = "castle principiantes";

        Assert.Equal("Fast Castle para principiantes (27 + 2)", Assert.Single(main.Entries).Name);
    }

    [Fact]
    public void Apoyar_abre_la_seccion_de_la_web()
    {
        var main = new MainViewModel(_data, new FakeDialogs());

        Assert.Equal("https://aoe2-bo-coach.vercel.app/#apoyar", main.SupportUrl);
    }

    [Fact]
    public void Filtrar_la_biblioteca_por_civ_deja_las_de_esa_civ_y_las_de_cualquiera()
    {
        // D7: una BO importada y una mejorada por civ, más los 3 ejemplos para cualquier civ.
        var main = new MainViewModel(_data, new FakeDialogs());

        main.CivFilter = "huns";
        Assert.Equal(5, main.Entries.Count);
        Assert.All(main.Entries, e => Assert.Contains(e.BuildOrder!.Civ, new[] { "huns", "any" }));

        main.CivFilter = MainViewModel.GameCivFilter;
        var civ = "franks";
        main.GameCiv = () => civ;
        main.GameCivChanged();
        Assert.Contains(main.Entries, e => e.BuildOrder!.Civ == "franks");
        Assert.DoesNotContain(main.Entries, e => e.BuildOrder!.Civ == "huns");

        main.GameCiv = () => null; // "la de la BO": no hay civ de la partida, se ven todas
        main.GameCivChanged();
        main.CivFilter = MainViewModel.AllCivs;
        Assert.True(main.Entries.Count > 100);
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
