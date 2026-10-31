using System.Collections.ObjectModel;
using BoCoach.App.Web;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Web;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoCoach.App.Coaching;

/// <summary>Los diálogos que necesita la sección "Build order" (los implementa la ventana).</summary>
public interface IBuildOrderDialogs
{
    bool Confirm(string message);

    /// <summary>Pide un texto (el código de una BO). null = cancelado.</summary>
    string? AskText(string title, string message, string initial = "");

    /// <summary>Muestra el diálogo "Cuenta de la web". null = cancelado.</summary>
    WebSettings? EditWebAccount(WebSettings current);

    void OpenUrl(string url);
}

/// <summary>Una BO de tu lista en la ventana.</summary>
public sealed partial class BuildOrderEntryViewModel(string code, string name, string detail) : ObservableObject
{
    public string Code { get; } = code;
    public string Name { get; } = name;

    /// <summary>"francos · Cicero7800" o "bizantinos · tuya, privada".</summary>
    public string Detail { get; } = detail;

    /// <summary>"guardada" o "se baja al usarla".</summary>
    [ObservableProperty] public partial string State { get; set; } = "";
}

/// <summary>
/// La sección "Build order" (parte 4): tu cuenta de la web, tu lista (tus BO y las suscriptas), sumar una por código y,
/// a mano, cuál usa el coach. Las BO se bajan recién cuando se usan y quedan guardadas (<see cref="BuildOrderCache"/>).
/// </summary>
public sealed partial class BuildOrdersViewModel : ObservableObject
{
    private readonly BuildOrderCache _cache;
    private readonly WebAccount _web;
    private readonly IBuildOrderDialogs _dialogs;
    private readonly GameData _data;
    private readonly SemaphoreSlim _imports = new(1, 1);
    private Task _choosing = Task.CompletedTask;

    public BuildOrdersViewModel(BuildOrderCache cache, WebAccount web, IBuildOrderDialogs dialogs, GameData data)
    {
        _cache = cache;
        _web = web;
        _dialogs = dialogs;
        _data = data;
        RebuildEntries();
    }

    public ObservableCollection<BuildOrderEntryViewModel> Entries { get; } = [];

    public bool Connected => _web.Settings.HasToken;

    public string AccountText => !Connected ? "Conectá tu cuenta para elegir BO"
        : _cache.List.Account.Length > 0 ? $"Conectada como {_cache.List.Account}"
        : "Cuenta conectada";

    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>La elegida a mano en la lista.</summary>
    [ObservableProperty] public partial BuildOrderEntryViewModel? SelectedEntry { get; set; }

    /// <summary>La BO de la elegida, ya bajada (null mientras se baja o si no se pudo).</summary>
    [ObservableProperty] public partial BuildOrder? SelectedBuildOrder { get; set; }

    /// <summary>La sección "Apoyar el proyecto" de la web (Cafecito, PayPal, Ko-fi, Mercado Pago y las metas).</summary>
    public string SupportUrl => new Uri(_web.Settings.BaseUri, "#apoyar").ToString();

    partial void OnSelectedEntryChanged(BuildOrderEntryViewModel? value)
    {
        SelectedBuildOrder = null;
        if (value is not null)
            _choosing = DownloadSelectedAsync(value);
    }

    /// <summary>Elige una BO de la lista y espera a que se baje (si hacía falta).</summary>
    public Task ChooseAsync(BuildOrderEntryViewModel entry)
    {
        if (SelectedEntry != entry)
            SelectedEntry = entry;
        return _choosing;
    }

    /// <summary>Elige la de ese código si está en la lista (la última usada al abrir la app).</summary>
    public void Select(string? code)
    {
        if (Entries.FirstOrDefault(e => e.Code == code) is { } entry)
            SelectedEntry = entry;
    }

    private async Task DownloadSelectedAsync(BuildOrderEntryViewModel entry)
    {
        if (!_cache.IsSaved(entry.Code))
            Status = $"Bajando \"{entry.Name}\"…";
        try
        {
            var bo = await _cache.GetAsync(entry.Code);
            if (SelectedEntry != entry)
                return;
            SelectedBuildOrder = bo;
            Status = $"Lista: \"{bo.Name}\".";
        }
        catch (WebLibraryException e)
        {
            Status = $"No se pudo bajar \"{entry.Name}\": {e.Message}";
        }
        UpdateStates();
    }

    /// <summary>Pide la lista a la web (sin internet, queda la guardada).</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!Connected)
        {
            Status = AccountText + ".";
            Changed();
            return;
        }
        var error = await _cache.RefreshAsync();
        RebuildEntries();
        Status = error ?? (Entries.Count == 0 ? "Tu lista está vacía: armá una BO en la web o sumá una con Pegar código." : $"{Entries.Count} BO en tu lista.");
    }

    /// <summary>Suma una BO por su código o el enlace de su página.</summary>
    [RelayCommand]
    public async Task PasteCodeAsync()
    {
        if (_dialogs.AskText("Sumar una BO", "El código de la BO (6 letras y números) o el enlace de su página en la web:") is not { } text)
            return;
        if (WebCode.Parse(text) is not { } code)
        {
            Status = $"\"{text.Trim()}\" no es un código de la web (son 6 letras y números).";
            return;
        }
        await ImportAsync(code, confirm: false);
    }

    /// <summary>Un enlace bocoach:// de la web: conectar la cuenta o sumar una BO (con confirmación).</summary>
    public Task HandleLinkAsync(string text)
    {
        switch (AppLink.Parse(text))
        {
            case AppLink.Connect connect:
                return ConnectWithTokenAsync(connect.Token);
            case AppLink.Import import:
                return ImportAsync(import.Code, confirm: true);
            default:
                Status = "Ese enlace no es de BO Coach.";
                return Task.CompletedTask;
        }
    }

    /// <summary>Guarda el token que manda la web ("Conectar la app") y pide la lista.</summary>
    public async Task ConnectWithTokenAsync(string token)
    {
        _web.Save(_web.Settings with { Token = token });
        await RefreshAsync();
        Changed();
        if (_cache.List.Account.Length > 0)
            Status = $"{AccountText}. {Status}";
    }

    /// <summary>
    /// Suma una BO: se suscribe (si no es propia), pide la lista y la baja. Desde un enlace pregunta antes con el nombre
    /// y la cuenta que la subió. De a una: la segunda espera a la primera.
    /// </summary>
    public async Task ImportAsync(string code, bool confirm)
    {
        if (!Connected)
        {
            Status = "Conectá tu cuenta para sumar BO.";
            return;
        }
        await _imports.WaitAsync();
        try
        {
            if (confirm)
            {
                var found = await _web.Client.DownloadAsync(code, _web.Settings.Token);
                if (!_dialogs.Confirm($"¿Sumar \"{found.BuildOrder.Name}\" (subida por {found.Author}) a tus BO?"))
                    return;
            }
            var entry = await _cache.AddAsync(code);
            RebuildEntries();
            Status = $"Sumada \"{entry?.Name ?? code}\" a tus BO.";
        }
        catch (WebLibraryException e)
        {
            Status = $"No se pudo sumar {code}: {e.Message}";
        }
        finally
        {
            _imports.Release();
        }
    }

    [RelayCommand]
    private void SearchMore() => _dialogs.OpenUrl(new Uri(_web.Settings.BaseUri, "biblioteca").ToString());

    /// <summary>Registra los enlaces bocoach:// (para que la web pueda abrir la app) y abre Mi cuenta en la web.</summary>
    [RelayCommand]
    private void Connect()
    {
        if (Environment.ProcessPath is { } exe)
            new LinkProtocol().Register(exe);
        _dialogs.OpenUrl(new Uri(_web.Settings.BaseUri, "cuenta").ToString());
        Status = "En la web, tocá \"Conectar la app\" en Mi cuenta.";
    }

    /// <summary>La ventana de hoy: dirección de la web y el token pegado a mano.</summary>
    [RelayCommand]
    private async Task AccountSettingsAsync()
    {
        if (_dialogs.EditWebAccount(_web.Settings) is not { } settings)
            return;
        _web.Save(settings);
        await RefreshAsync();
        Changed();
    }

    private void RebuildEntries()
    {
        var selected = SelectedEntry?.Code;
        Entries.Clear();
        foreach (var item in _cache.List.Items)
            Entries.Add(new BuildOrderEntryViewModel(item.Code, item.Name, Detail(item)));
        UpdateStates();
        if (selected is not null)
        {
            var again = Entries.FirstOrDefault(e => e.Code == selected);
            // Sin volver a bajar: es la misma.
            var bo = SelectedBuildOrder;
            SelectedEntry = again;
            if (again is not null && bo is not null)
                SelectedBuildOrder = bo;
        }
        Changed();
    }

    private void UpdateStates()
    {
        foreach (var entry in Entries)
            entry.State = _cache.IsSaved(entry.Code) ? "guardada" : "se baja al usarla";
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Connected));
        OnPropertyChanged(nameof(AccountText));
    }

    private string Detail(WebBuildOrderEntry item)
    {
        var civ = item.Civ == "any" ? "cualquier civ"
            : _data.Civ(item.Civ).Names.TryGetValue("mx", out var name) ? name.ToLowerInvariant() : item.Civ;
        var who = item.Own ? (item.Visibility == "private" ? "tuya, privada" : "tuya") : item.GuideAuthor;
        return $"{civ} · {who}";
    }
}
