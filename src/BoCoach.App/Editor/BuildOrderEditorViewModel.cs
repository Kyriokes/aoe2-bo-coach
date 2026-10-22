using System.Collections.ObjectModel;
using System.ComponentModel;
using BoCoach.Core;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Editor;

/// <summary>
/// Edita una BO. Después de cada cambio la valida y la simula con el Planner de Core (vista previa):
/// tiempos estimados por paso, bloqueos y desvío contra los objetivos.
/// </summary>
public sealed partial class BuildOrderEditorViewModel : ObservableObject
{
    private static readonly HashSet<string> InputProperties =
    [
        nameof(Name), nameof(Civ), nameof(Map), nameof(Author), nameof(Description), nameof(TagsText),
        nameof(FeudalTargetText), nameof(CastleTargetText),
        nameof(FeudalFood), nameof(FeudalWood), nameof(FeudalGold), nameof(FeudalStone),
        nameof(PostureVillagers), nameof(PostureUnit), nameof(PostureNextAge),
        nameof(BranchBeforeText), nameof(BranchNote),
    ];

    /// <summary>La BO principal en <see cref="SelectedBranch"/>; las ramas van por su señal.</summary>
    public const string MainSteps = "";

    private static readonly string[] ResearchDefaults = ["loom", "feudal_age", "castle_age", "imperial_age"];

    private readonly CoachData _app;
    private readonly IReadOnlyList<Option<string?>> _confirmOptions;
    private readonly int _schemaVersion;
    private readonly bool _hadPosture;

    /// <summary>Aldeanos al llegar a Castillos: el editor no los muestra, pero se guardan como venían.</summary>
    private readonly VillagerDistribution? _villagersAtCastle;

    // D3: los pasos de la BO principal y de cada rama; la lista que se ve (Steps) es la de SelectedBranch.
    private readonly Dictionary<string, List<StepViewModel>> _stepLists = [];
    private readonly Dictionary<string, (string Before, string Note)> _branchInfo = [];
    private bool _loading = true;

    public BuildOrderEditorViewModel(BuildOrder bo, CoachData app)
    {
        _app = app;
        _schemaVersion = bo.SchemaVersion;
        _confirmOptions = new[] { new Option<string?>(null, "(por convención)") }
            .Concat(app.Events.Phrases.Keys.Order().Select(id => new Option<string?>(id, id)))
            .ToList();

        Id = bo.Id;
        Name = bo.Name;
        Civ = bo.Civ;
        Map = Maps.IsAny(bo.Map) ? Maps.Any : bo.Map!;
        MapOptions = app.Maps.All.Select(m => new Option<string>(m.Key, m.Name)).ToList();
        if (MapOptions.All(o => o.Value != Map))
            MapOptions = [.. MapOptions, new Option<string>(Map, $"{Map} (no está en maps.json)")];
        Author = bo.Author ?? "";
        Description = bo.Description ?? "";
        TagsText = string.Join(", ", bo.Tags);
        FeudalTargetText = bo.Targets?.FeudalClickTime is { } f ? GameClock.Format(f) : "";
        CastleTargetText = bo.Targets?.CastleClickTime is { } c ? GameClock.Format(c) : "";
        FeudalFood = bo.Targets?.VillagersAtFeudal?.Food;
        FeudalWood = bo.Targets?.VillagersAtFeudal?.Wood;
        FeudalGold = bo.Targets?.VillagersAtFeudal?.Gold;
        FeudalStone = bo.Targets?.VillagersAtFeudal?.Stone;
        // La postura escrita o, si la BO no tiene, la deducida de sus pasos (se guarda solo si se cambia).
        _hadPosture = bo.Posture is not null;
        _villagersAtCastle = bo.Targets?.VillagersAtCastle;
        var (posture, _) = Posture.Effective(bo);
        PostureVillagers = posture.Queue.Contains("villager");
        PostureUnit = posture.Queue.FirstOrDefault(u => u != "villager");
        PostureNextAge = posture.NextAge;
        PostureUnitOptions = new[] { new Option<string?>(null, "(ninguna)") }
            .Concat(app.GameData.Entities.Values
                .Where(e => e.Kind == EntityKind.Unit && e.MadeIn is not null && e.Key != "villager")
                .Select(e => new Option<string?>(e.Key, StepText.Name(e.Key)))
                .OrderBy(o => o.Label, StringComparer.CurrentCulture))
            .ToList();
        Steps = new ObservableCollection<StepViewModel>(StepModels(bo.Steps));
        foreach (var branch in bo.Branches ?? [])
        {
            _stepLists[branch.On] = StepModels(branch.Steps);
            _branchInfo[branch.On] = (branch.Before is { } b ? GameClock.Format(b) : "", branch.Note ?? "");
        }
        BranchOptions = [new(MainSteps, "BO principal"), .. Branch.Signals.Select(s => new Option<string>(s.Key, s.Value))];
        SelectedBranch = MainSteps;
        BranchBeforeText = "";
        BranchNote = "";
        Steps.CollectionChanged += (_, _) => Changed();
        _loading = false;
        Refresh();
    }

    public string Id { get; private set; }

    /// <summary>"any" y las civs de los datos del juego (la vista previa usa sus bonificaciones).</summary>
    public IReadOnlyList<string> CivOptions => ["any", .. _app.GameData.Civs.Keys.Order()];

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Civ { get; set; }

    /// <summary>D8: el mapa de la BO (clave de data/maps.json; "any" es cualquiera y no se guarda).</summary>
    [ObservableProperty] public partial string Map { get; set; }

    /// <summary>Los mapas de data/maps.json ("any" primero), más el de la BO si no está en el archivo.</summary>
    public IReadOnlyList<Option<string>> MapOptions { get; }

    [ObservableProperty] public partial string Author { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty] public partial string TagsText { get; set; }
    [ObservableProperty] public partial string FeudalTargetText { get; set; }
    [ObservableProperty] public partial string CastleTargetText { get; set; }
    [ObservableProperty] public partial int? FeudalFood { get; set; }
    [ObservableProperty] public partial int? FeudalWood { get; set; }
    [ObservableProperty] public partial int? FeudalGold { get; set; }
    [ObservableProperty] public partial int? FeudalStone { get; set; }

    /// <summary>Postura de cierre (D2): aldeanos siempre en cola.</summary>
    [ObservableProperty] public partial bool PostureVillagers { get; set; }

    /// <summary>Postura de cierre: la unidad militar siempre en cola (en cada edificio que la produce), o null.</summary>
    [ObservableProperty] public partial string? PostureUnit { get; set; }

    /// <summary>Postura de cierre: la edad a la que se apunta con lo que sobra, o null.</summary>
    [ObservableProperty] public partial string? PostureNextAge { get; set; }

    public IReadOnlyList<Option<string?>> PostureUnitOptions { get; }

    public IReadOnlyList<Option<string?>> PostureAgeOptions { get; } =
    [
        new(null, "(ninguna)"),
        new("feudal_age", "Feudal"),
        new("castle_age", "Castillos"),
        new("imperial_age", "Imperial"),
    ];

    public ObservableCollection<StepViewModel> Steps { get; }

    /// <summary>De qué se ven los pasos: la BO principal o una rama (D3, por su señal).</summary>
    public IReadOnlyList<Option<string>> BranchOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBranch))]
    public partial string SelectedBranch { get; set; }

    public bool IsBranch => SelectedBranch != MainSteps;

    /// <summary>La rama solo vale si la señal llega antes de este momento (mm:ss; vacío = siempre).</summary>
    [ObservableProperty] public partial string BranchBeforeText { get; set; }

    /// <summary>Qué hacer y por qué, para el overlay.</summary>
    [ObservableProperty] public partial string BranchNote { get; set; }

    partial void OnSelectedBranchChanging(string oldValue, string newValue)
    {
        if (oldValue is null || _loading)
            return;
        _stepLists[oldValue] = [.. Steps];
        if (oldValue != MainSteps)
            _branchInfo[oldValue] = (BranchBeforeText, BranchNote);
    }

    partial void OnSelectedBranchChanged(string value)
    {
        if (_loading)
            return;
        var loading = _loading;
        _loading = true;
        Steps.Clear();
        foreach (var step in _stepLists.GetValueOrDefault(value) ?? [])
            Steps.Add(step);
        (BranchBeforeText, BranchNote) = _branchInfo.GetValueOrDefault(value, ("", ""));
        SelectedStep = null;
        _loading = loading;
        Refresh();
    }

    private List<StepViewModel> StepModels(IReadOnlyList<Step> steps)
    {
        var models = steps.Select(NewStep).ToList();
        for (var i = 1; i < models.Count; i++)
            models[i].WithPrevious = steps[i].Group is { } group && group == steps[i - 1].Group;
        return models;
    }

    /// <summary>Los pasos de una lista del editor, con los grupos de los pasos mixtos armados.</summary>
    private static List<Step> ToSteps(IReadOnlyList<StepViewModel> models)
    {
        // Pasos mixtos: cada paso "junto con el anterior" toma el grupo del anterior (o le crea uno con su id).
        var steps = models.Select(s => s.ToStep()).ToList();
        for (var i = 1; i < steps.Count; i++)
        {
            if (!models[i].WithPrevious)
                continue;
            steps[i - 1].Group ??= $"g{steps[i - 1].Id}";
            steps[i].Group = steps[i - 1].Group;
        }
        return steps;
    }

    /// <summary>Los pasos de una lista: la que se ve si es la elegida, si no la guardada.</summary>
    private IReadOnlyList<StepViewModel> ListOf(string key) =>
        key == SelectedBranch ? Steps : _stepLists.GetValueOrDefault(key) ?? [];

    /// <summary>Los ids de todos los pasos (principal y ramas), para que uno nuevo no repita ninguno.</summary>
    private IEnumerable<string> AllStepIds() =>
        new[] { MainSteps }.Concat(Branch.Signals.Keys).SelectMany(ListOf).Select(s => s.Id);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateStepCommand), nameof(RemoveStepCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial StepViewModel? SelectedStep { get; set; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool HasErrors { get; set; }

    /// <summary>Errores y avisos de validación, uno por línea.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>Resumen de la vista previa: clicks estimados, bloqueos y final.</summary>
    public ObservableCollection<string> Summary { get; } = [];

    public PlanResult? LastPlan { get; private set; }

    public BuildOrder ToBuildOrder()
    {
        VillagerDistribution? atFeudal = FeudalFood is null && FeudalWood is null && FeudalGold is null && FeudalStone is null
            ? null
            : new VillagerDistribution(FeudalFood ?? 0, FeudalWood ?? 0, FeudalGold ?? 0, FeudalStone ?? 0);
        int? feudal = GameClock.TryParse(FeudalTargetText, out var f) ? f : null;
        int? castle = GameClock.TryParse(CastleTargetText, out var c) ? c : null;

        var steps = ToSteps(ListOf(MainSteps));
        var branches = Branch.Signals.Keys
            .Where(key => ListOf(key).Count > 0)
            .Select(key =>
            {
                var (before, note) = key == SelectedBranch ? (BranchBeforeText, BranchNote) : _branchInfo.GetValueOrDefault(key, ("", ""));
                return new Branch
                {
                    On = key,
                    Before = GameClock.TryParse(before, out var b) ? b : null,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                    Steps = ToSteps(ListOf(key)),
                };
            })
            .ToList();

        var bo = new BuildOrder
        {
            SchemaVersion = _schemaVersion,
            Id = Id,
            Name = Name.Trim(),
            Civ = string.IsNullOrWhiteSpace(Civ) ? "any" : Civ.Trim(),
            Map = Maps.IsAny(Map) ? null : Map.Trim(),
            Author = string.IsNullOrWhiteSpace(Author) ? null : Author.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Steps = steps,
            Branches = branches.Count > 0 ? branches : null,
            Targets = feudal is null && castle is null && atFeudal is null && _villagersAtCastle is null
                ? null
                : new Targets { FeudalClickTime = feudal, CastleClickTime = castle, VillagersAtFeudal = atFeudal, VillagersAtCastle = _villagersAtCastle },
        };
        var posture = new ClosingPosture
        {
            Queue = [.. PostureVillagers ? ["villager"] : Array.Empty<string>(), .. PostureUnit is { } unit ? [unit] : Array.Empty<string>()],
            NextAge = PostureNextAge,
        };
        // Si la BO no traía postura y queda igual a la deducida, no se escribe: así sigue a los pasos si cambian.
        var derived = Posture.Effective(bo).Posture;
        var same = posture.Queue.SequenceEqual(derived.Queue) && posture.NextAge == derived.NextAge;
        return bo with { Posture = !_hadPosture && same ? null : posture };
    }

    public void MarkSaved(BuildOrder saved)
    {
        Id = saved.Id;
        IsDirty = false;
    }

    // ---- Comandos de pasos ----

    [RelayCommand]
    private void AddVillagers()
    {
        var lastAssign = Steps.LastOrDefault(s => s.Action == StepAction.Train && s.What == "villager")?.Assign;
        Insert(new Step { Do = StepAction.Train, What = "villager", Count = 1, Assign = lastAssign ?? GatherSource.Sheep });
    }

    [RelayCommand]
    private void AddBuild() =>
        Insert(new Step { Do = StepAction.Build, What = "house", Count = 1, From = GatherSource.Wood, Assign = GatherSource.Wood });

    [RelayCommand]
    private void AddResearch()
    {
        var used = Steps.Where(s => s.Action == StepAction.Research).Select(s => s.What).ToHashSet();
        Insert(new Step { Do = StepAction.Research, What = ResearchDefaults.FirstOrDefault(t => !used.Contains(t)) ?? "loom" });
    }

    [RelayCommand]
    private void AddMove() =>
        Insert(new Step { Do = StepAction.Move, Count = 1, From = GatherSource.Wood, Assign = GatherSource.Gold });

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateStep()
    {
        if (SelectedStep is { } step)
            Insert(step.ToStep());
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveStep()
    {
        if (SelectedStep is not { } step)
            return;
        var index = Steps.IndexOf(step);
        Steps.RemoveAt(index);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => MoveSelected(+1);

    /// <summary>Reordenamiento por arrastre: mueve el paso de una posición a otra.</summary>
    public void MoveStep(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Steps.Count || to >= Steps.Count)
            return;
        Steps.Move(from, to);
    }

    private bool HasSelection() => SelectedStep is not null;

    private void MoveSelected(int delta)
    {
        if (SelectedStep is not { } step)
            return;
        var index = Steps.IndexOf(step);
        MoveStep(index, Math.Clamp(index + delta, 0, Steps.Count - 1));
        SelectedStep = step;
    }

    private void Insert(Step step)
    {
        var vm = NewStep(step with { Id = StepIds.Next(AllStepIds()) });
        var index = SelectedStep is { } selected ? Steps.IndexOf(selected) + 1 : Steps.Count;
        Steps.Insert(index, vm);
        SelectedStep = vm;
    }

    private StepViewModel NewStep(Step step) => new(step, _app.GameData, _confirmOptions, _ => Changed());

    // ---- Validación y vista previa ----

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && InputProperties.Contains(name))
            Changed();
    }

    private void Changed()
    {
        if (_loading)
            return;
        IsDirty = true;
        Refresh();
    }

    private void Refresh()
    {
        for (var i = 0; i < Steps.Count; i++)
            Steps[i].Number = i + 1;

        var bo = ToBuildOrder();
        var validation = _app.Validator.Validate(bo);
        HasErrors = !validation.IsValid;

        Problems.Clear();
        foreach (var step in Steps)
        {
            var issues = validation.Issues.Where(i => i.StepId == step.Id).ToList();
            step.IssueText = issues.Count == 0 ? null : string.Join("\n", issues.Select(i => i.Message));
            step.HasError = issues.Any(i => i.Severity == Severity.Error);
            step.HasWarning = !step.HasError && issues.Count > 0;
        }
        foreach (var issue in validation.Issues)
        {
            var number = issue.StepId is { } id ? Steps.FirstOrDefault(s => s.Id == id)?.Number : null;
            var where = number is null ? "" : $" (paso {number})";
            Problems.Add($"{(issue.Severity == Severity.Error ? "Error" : "Aviso")}{where}: {issue.Message}");
        }

        Summary.Clear();
        // Las ramas no tienen vista previa propia: se hacen cuando llega su señal, en cualquier momento.
        if (IsBranch)
        {
            foreach (var step in Steps)
                ClearEstimate(step);
            Summary.Add($"{Branch.Signals[SelectedBranch]}: estos pasos van primero en el overlay cuando el coach ve la señal (una vez por partida).");
            return;
        }
        if (!validation.IsValid || Steps.Count == 0)
        {
            LastPlan = null;
            foreach (var step in Steps)
                ClearEstimate(step);
            Summary.Add(Steps.Count == 0 ? "Agregá pasos para ver la vista previa." : "Corregí los errores para ver la vista previa.");
            return;
        }

        var plan = _app.Planner.Simulate(bo);
        LastPlan = plan;
        ShowEstimates(plan);
        ShowSummary(bo, plan);
    }

    private static void ClearEstimate(StepViewModel step)
    {
        step.EstimateText = "";
        step.BlockText = null;
        step.IsPopBlocked = false;
        step.BarStart = 0;
        step.BarWidth = 0;
        step.BarRest = 1;
    }

    private void ShowEstimates(PlanResult plan)
    {
        // Escala de la línea de tiempo: hasta el último paso que termina, o un minuto después del último
        // bloqueo que no se destraba (si no, todo queda aplastado contra el horizonte de la simulación).
        var lastEvent = plan.Steps.Select(s => s.End ?? s.Start ?? 0)
            .Concat(plan.Blocks.Select(b => Unresolved(plan, b) ? b.From + 60 : b.To))
            .DefaultIfEmpty(0)
            .Max();
        var total = Math.Max(1, lastEvent);
        for (var i = 0; i < Steps.Count; i++)
        {
            var vm = Steps[i];
            var estimate = plan.Steps[i];
            var start = Math.Min(estimate.Start ?? total, total);
            var end = Math.Min(estimate.End ?? total, total);
            vm.EstimateText = estimate.Start is null
                ? "no arranca"
                : $"{GameClock.Format(start)} → {(estimate.End is null ? "no termina" : GameClock.Format(end))}";
            vm.BarStart = start / total;
            vm.BarWidth = Math.Max(0.006, (end - start) / total);
            vm.BarRest = Math.Max(0, 1 - vm.BarStart - vm.BarWidth);

            var blocks = plan.Blocks.Where(b => b.StepId == vm.Id && b.Duration >= 3).ToList();
            vm.IsPopBlocked = blocks.Any(b => b.Reason == BlockReason.Population);
            vm.BlockText = blocks.Count == 0 ? null : string.Join("\n", blocks.Select(b => Describe(plan, b)));
        }
    }

    /// <summary>Un bloqueo que dura hasta el final de la simulación: el paso nunca se destraba.</summary>
    private static bool Unresolved(PlanResult plan, PlanBlock block) =>
        plan.Timeline.Count > 0 && block.To >= plan.Timeline[^1].Time;

    private void ShowSummary(BuildOrder bo, PlanResult plan)
    {
        foreach (var (tech, label, target) in new[]
                 {
                     ("feudal_age", "Feudal", bo.Targets?.FeudalClickTime),
                     ("castle_age", "Castillos", bo.Targets?.CastleClickTime),
                 })
        {
            if (plan.ClickTime(tech) is not { } click)
                continue;
            var line = $"{label}: click estimado {GameClock.Format(click)}";
            if (target is { } t)
            {
                var delta = click - t;
                line += $" · objetivo {GameClock.Format(t)} ({GameClock.Format(Math.Abs(delta))} {(delta <= 0 ? "antes" : "tarde")})";
            }
            Summary.Add(line);
        }

        foreach (var block in plan.Blocks.Where(b => b.Reason == BlockReason.Population && b.Duration >= 3))
        {
            var number = Steps.FirstOrDefault(s => s.Id == block.StepId)?.Number;
            var howLong = Unresolved(plan, block)
                ? $"desde {GameClock.Format(block.From)} y no se destraba"
                : $"{GameClock.Format(block.Duration)} desde {GameClock.Format(block.From)}";
            Summary.Add($"Bloqueo de población en el paso {number}: {howLong}. Falta una casa antes.");
        }

        if (!plan.Completed)
        {
            var stuck = plan.Steps.Select((s, i) => (s, i)).First(x => x.s.End is null);
            var reason = plan.Blocks.LastOrDefault(b => b.StepId == stuck.s.Step.Id);
            Summary.Add($"La BO no termina: el paso {stuck.i + 1} queda trabado{(reason is null ? "" : $" ({Describe(plan, reason).ToLowerInvariant()})")}.");
        }
        else if (plan.Timeline.Count > 0)
        {
            var last = plan.Timeline[^1];
            Summary.Add($"Termina en {GameClock.Format(last.Time)} con {last.Villagers} aldeanos y población {last.Population}/{last.PopulationCap}.");
        }
    }

    private static string Describe(PlanResult plan, PlanBlock block)
    {
        var what = block.Reason switch
        {
            BlockReason.Population => "Sin población",
            BlockReason.Resources => $"Esperando {(block.Missing is { } r ? Core.Economy.Suggestion.Name(r) : "recursos")}",
            BlockReason.Producer => "Falta el edificio",
            BlockReason.Villagers => "Sin aldeanos disponibles",
            _ => "Esperando la edad",
        };
        return Unresolved(plan, block)
            ? $"{what} desde {GameClock.Format(block.From)}, no se destraba"
            : $"{what} {GameClock.Format(block.Duration)} desde {GameClock.Format(block.From)}";
    }
}
