using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.App.Tests;

/// <summary>
/// Las BO del repo (ejemplos y una por civ) como si fueran tu lista de la web. Por defecto todas están en la lista y
/// guardadas; se puede elegir cuáles están en la lista, cuáles guardadas y cuáles no se pueden bajar (sin internet).
/// </summary>
internal sealed class RepoBuildOrders(Func<string, bool>? inList = null, Func<string, bool>? saved = null, ISet<string>? failing = null)
    : IBuildOrderSource
{
    private static readonly BuildOrder[] All = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "build_orders"), "*.json")
        .Select(BuildOrderSerializer.Load).ToArray();

    public static BuildOrder Get(string id) => All.Single(b => b.Id == id);

    public WebBuildOrderList List { get; } = new("Prueba", All.Where(bo => inList?.Invoke(bo.Id) ?? true).Select(bo =>
    {
        var s = BuildOrderSummary.Of(bo);
        return new WebBuildOrderEntry(bo.Id, s.Name, s.Civ, s.Map, s.Style, s.Tags, "Prueba", "Prueba", true, "public", DateTimeOffset.UnixEpoch, null, null);
    }).ToList());

    public BuildOrder? Saved(string code) => AllSaved().FirstOrDefault(b => b.Id == code);
    public IEnumerable<BuildOrder> AllSaved() => All.Where(bo => saved?.Invoke(bo.Id) ?? true);

    public Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default) =>
        failing?.Contains(code) == true
            ? Task.FromException<BuildOrder>(new WebLibraryException("sin conexión"))
            : Task.FromResult(All.Single(b => b.Id == code));
}
