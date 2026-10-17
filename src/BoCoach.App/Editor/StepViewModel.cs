using System.ComponentModel;
using BoCoach.Core;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoCoach.App.Editor;

/// <summary>Un paso editable. Cada cambio de un campo avisa al editor para revalidar y re-simular.</summary>
public sealed partial class StepViewModel : ObservableObject
{
    private static readonly HashSet<string> InputProperties =
    [
        nameof(Id), nameof(Action), nameof(What), nameof(Count), nameof(Assign), nameof(From), nameof(Builders),
        nameof(AfterText), nameof(AtPop), nameof(AtTimeText), nameof(ConfirmBy), nameof(Note), nameof(WithPrevious),
    ];

    private readonly GameData _data;
    private readonly Action<StepViewModel> _changed;
    private bool _loading;

    public StepViewModel(Step step, GameData data, IReadOnlyList<Option<string?>> confirmOptions, Action<StepViewModel> changed)
    {
        _data = data;
        _changed = changed;
        ConfirmOptions = confirmOptions;
        _loading = true;
        Id = step.Id;
        Action = step.Do;
        What = step.What;
        Count = step.Count;
        Assign = step.Assign;
        From = step.From;
        Builders = step.Builders;
        AfterText = string.Join(", ", step.After);
        AtPop = step.AtPop;
        AtTimeText = step.AtTime is { } t ? GameClock.Format(t) : "";
        ConfirmBy = step.ConfirmBy;
        Note = step.Note ?? "";
        _loading = false;
    }

    [ObservableProperty] public partial string Id { get; set; }
    [ObservableProperty] public partial StepAction Action { get; set; }
    [ObservableProperty] public partial string? What { get; set; }
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial GatherSource? Assign { get; set; }
    [ObservableProperty] public partial GatherSource? From { get; set; }
    [ObservableProperty] public partial int? Builders { get; set; }
    [ObservableProperty] public partial string AfterText { get; set; }
    [ObservableProperty] public partial int? AtPop { get; set; }
    [ObservableProperty] public partial string AtTimeText { get; set; }
    [ObservableProperty] public partial string? ConfirmBy { get; set; }
    [ObservableProperty] public partial string Note { get; set; }

    /// <summary>
    /// Paso mixto: esta meta se hace a la vez que el paso anterior (mismo <see cref="Step.Group"/>). El grupo se arma al
    /// guardar (<see cref="BuildOrderEditorViewModel.ToBuildOrder"/>).
    /// </summary>
    [ObservableProperty] public partial bool WithPrevious { get; set; }

    // Lo que calcula el editor (validación y vista previa).
    [ObservableProperty] public partial int Number { get; set; }
    [ObservableProperty] public partial string EstimateText { get; set; } = "";
    [ObservableProperty] public partial string? IssueText { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial bool HasWarning { get; set; }
    [ObservableProperty] public partial bool IsPopBlocked { get; set; }
    [ObservableProperty] public partial string? BlockText { get; set; }
    [ObservableProperty] public partial double BarStart { get; set; }
    [ObservableProperty] public partial double BarWidth { get; set; }
    [ObservableProperty] public partial double BarRest { get; set; } = 1;

    /// <summary>Lo que se ve en la lista de pasos; "+" adelante si va junto con el anterior (paso mixto).</summary>
    public string Description => (WithPrevious ? "+ " : "") + StepText.Describe(ToStep());

    public IReadOnlyList<Option<string?>> EntityOptions => Options.EntitiesFor(_data, Action);
    public IReadOnlyList<Option<string?>> ConfirmOptions { get; }

    public bool IsMove => Action == StepAction.Move;
    public bool IsBuild => Action == StepAction.Build;
    public bool UsesEntity => Action != StepAction.Move;
    public bool UsesFrom => Action is StepAction.Move or StepAction.Build;
    public bool UsesAssign => Action is StepAction.Move or StepAction.Build || (Action == StepAction.Train && What == "villager");

    public Step ToStep() => new()
    {
        Id = Id.Trim(),
        Do = Action,
        What = Action == StepAction.Move ? null : What,
        Count = Count,
        Assign = UsesAssign ? Assign : null,
        From = UsesFrom ? From : null,
        Builders = Action == StepAction.Build ? Builders : null,
        After = AfterText.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        AtPop = AtPop,
        AtTime = GameClock.TryParse(AtTimeText, out var t) ? t : null,
        ConfirmBy = string.IsNullOrWhiteSpace(ConfirmBy) ? null : ConfirmBy,
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
    };

    partial void OnActionChanged(StepAction value)
    {
        OnPropertyChanged(nameof(EntityOptions));
        OnPropertyChanged(nameof(IsMove));
        OnPropertyChanged(nameof(IsBuild));
        OnPropertyChanged(nameof(UsesEntity));
        OnPropertyChanged(nameof(UsesFrom));
        OnPropertyChanged(nameof(UsesAssign));
        if (!_loading && !EntityOptions.Any(o => o.Value == What))
            What = EntityOptions.FirstOrDefault()?.Value;
    }

    partial void OnWhatChanged(string? value) => OnPropertyChanged(nameof(UsesAssign));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } name && InputProperties.Contains(name))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));
            _changed(this);
        }
    }
}
