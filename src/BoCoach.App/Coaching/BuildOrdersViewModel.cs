using BoCoach.Core;
using System.IO;
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

    /// <summary>Una pregunta donde equivocarse sale caro: Enter responde que no.</summary>
    bool ConfirmNoByDefault(string message);

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

    public string AccountText => !Connected ? Texts.AccountNotConnected
        : _cache.List.Account.Length > 0 ? Texts.AccountConnectedAs(_cache.List.Account)
        : Texts.AccountConnected;

    /// <summary>Lo que dice la lista mientras está vacía.</summary>
    public string EmptyListText => Connected ? Texts.ListEmpty : Texts.ListEmptyNoAccount;

    public bool HasEntries => Entries.Count > 0;

    [ObservableProperty] public partial string Status { get; set; } = "";

    /// <summary>La elegida a mano en la lista.</summary>
    [ObservableProperty] public partial BuildOrderEntryViewModel? SelectedEntry { get; set; }

    /// <summary>La BO de la elegida, ya bajada (null mientras se baja o si no se pudo).</summary>
    [ObservableProperty] public partial BuildOrder? SelectedBuildOrder { get; set; }

    /// <summary>La sección "Apoyar el proyecto" de la web (Cafecito, PayPal, Ko-fi, Mercado Pago y las metas).</summary>
    public string SupportUrl => new Uri(_web.Settings.BaseUri, Texts.WebPath("") + "#apoyar").ToString();

    partial void OnSelectedEntryChanged(BuildOrderEntryViewModel? value)
    {
        if (_rebuilding)
            return;
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
            Status = Texts.Downloading(entry.Name);
        try
        {
            var bo = await _cache.GetAsync(entry.Code);
            if (SelectedEntry != entry)
                return;
            SelectedBuildOrder = bo;
            Status = Texts.Ready(bo.Name);
        }
        catch (WebLibraryException e)
        {
            Status = Texts.DownloadFailed(entry.Name, e.Message);
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
        Status = error ?? (Entries.Count == 0 ? Texts.ListEmpty : Texts.ListCount(Entries.Count));
        await RedownloadStaleAsync();
    }

    /// <summary>
    /// La elegida, si la web la tiene más nueva que la guardada (republicada), se baja de nuevo: si no, el coach seguía
    /// con la vieja (partida 20261102-212823).
    /// </summary>
    private async Task RedownloadStaleAsync()
    {
        if (SelectedEntry is { } entry && SelectedBuildOrder is not null && !_cache.IsFresh(entry.Code))
        {
            _choosing = DownloadSelectedAsync(entry);
            await _choosing;
        }
    }

    /// <summary>Pide la lista sin tocar el estado de la ventana (al empezar el coach, que escribe el suyo).</summary>
    public async Task RefreshQuietlyAsync()
    {
        if (!Connected)
            return;
        try
        {
            if (await _cache.RefreshAsync() is null)
            {
                RebuildEntries();
                await RedownloadStaleAsync();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Suma una BO por su código o el enlace de su página.</summary>
    [RelayCommand]
    public async Task PasteCodeAsync()
    {
        if (_dialogs.AskText(Texts.AddBuildOrderTitle, Texts.AddBuildOrderPrompt) is not { } text)
            return;
        // El token de la app es un secreto: si eso es lo que se pegó, se conecta con él y no se repite en la barra de estado.
        if (AppLink.TokenIn(text) is { } token)
        {
            await ConnectWithTokenAsync(token);
            return;
        }
        if (WebCode.Parse(text) is not { } code)
        {
            Status = AppLink.LooksLikeToken(text) ? Texts.NotAWholeToken : Texts.NotACode(text.Trim());
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
                Status = Texts.ForeignLink;
                return Task.CompletedTask;
        }
    }

    /// <summary>
    /// El token que manda la web ("Conectar la app"): pregunta con el nombre de la cuenta antes de guardarlo (cualquier
    /// página puede abrir un enlace bocoach://conectar con el token de otra cuenta) y pide la lista. Si ya hay una cuenta
    /// conectada nombra las dos, y por defecto la respuesta es no: otra cuenta puede llamarse igual que la tuya.
    /// </summary>
    public async Task ConnectWithTokenAsync(string token)
    {
        await _imports.WaitAsync();
        try
        {
            WebBuildOrderList list;
            try
            {
                list = await _web.Client.MyBuildOrdersAsync(token);
            }
            catch (WebLibraryException e)
            {
                Status = Texts.ConnectFailed(e.Message);
                return;
            }
            var account = list.Account.Length > 0 ? list.Account : Texts.NoAccountName;
            var replaces = _web.Settings.HasToken && _web.Settings.Token != token;
            var accepted = replaces
                ? _dialogs.ConfirmNoByDefault(Texts.ConfirmReplaceAccount(_cache.List.Account.Length > 0 ? _cache.List.Account : Texts.NoAccountName, account))
                : _dialogs.Confirm(Texts.ConfirmConnect(account));
            if (!accepted)
                return;
            _web.Save(_web.Settings with { Token = token });
        }
        finally
        {
            _imports.Release();
        }
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
            Status = Texts.ConnectToAdd;
            return;
        }
        await _imports.WaitAsync();
        try
        {
            if (confirm)
            {
                var found = await _web.Client.DownloadAsync(code, _web.Settings.Token);
                if (!_dialogs.Confirm(Texts.ConfirmAdd(found.BuildOrder.Name, found.Author)))
                    return;
            }
            var entry = await _cache.AddAsync(code);
            RebuildEntries();
            Status = Texts.Added(entry?.Name ?? code);
        }
        catch (WebLibraryException e)
        {
            Status = Texts.AddFailed(code, e.Message);
        }
        finally
        {
            _imports.Release();
        }
    }

    [RelayCommand]
    private void SearchMore() => _dialogs.OpenUrl(new Uri(_web.Settings.BaseUri, Texts.WebPath("biblioteca")).ToString());

    /// <summary>Registra los enlaces bocoach:// (para que la web pueda abrir la app) y abre Mi cuenta en la web.</summary>
    [RelayCommand]
    private void Connect()
    {
        if (Environment.ProcessPath is { } exe)
            new LinkProtocol().Register(exe);
        _dialogs.OpenUrl(new Uri(_web.Settings.BaseUri, Texts.WebPath("cuenta")).ToString());
        Status = Texts.ConnectInWeb;
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
        // Antes de vaciar la lista: la ventana pone la elegida en null al vaciarla.
        var (selected, bo) = (SelectedEntry?.Code, SelectedBuildOrder);
        _rebuilding = true;
        try
        {
            Entries.Clear();
            foreach (var item in _cache.List.Items)
                Entries.Add(new BuildOrderEntryViewModel(item.Code, item.Name, Detail(item)));
            // La misma de antes, sin bajarla de nuevo (se sigue usando la que había).
            SelectedEntry = Entries.FirstOrDefault(e => e.Code == selected);
            SelectedBuildOrder = SelectedEntry is null ? null : bo;
        }
        finally
        {
            _rebuilding = false;
        }
        UpdateStates();
        Changed();
    }

    private bool _rebuilding;

    private void UpdateStates()
    {
        foreach (var entry in Entries)
            entry.State = _cache.IsSaved(entry.Code) ? Texts.EntrySaved : Texts.EntryNotSaved;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Connected));
        OnPropertyChanged(nameof(AccountText));
        OnPropertyChanged(nameof(EmptyListText));
        OnPropertyChanged(nameof(HasEntries));
    }

    private string Detail(WebBuildOrderEntry item)
    {
        var civ = item.Civ == "any" ? Texts.CivAny
            : _data.Civ(item.Civ).Label is { } name ? (Lang.IsEnglish ? name : name.ToLowerInvariant()) : item.Civ;
        var who = item.Own ? (item.Visibility == "private" ? Texts.OwnPrivate : Texts.Own) : item.GuideAuthor;
        return $"{civ} · {who}";
    }
}
