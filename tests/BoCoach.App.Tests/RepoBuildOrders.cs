using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.App.Tests;

/// <summary>Las BO del repo (ejemplos y una por civ) como si fueran tu lista de la web, todas guardadas.</summary>
internal sealed class RepoBuildOrders : IBuildOrderSource
{
    private static readonly BuildOrder[] All = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "build_orders"), "*.json")
        .Select(BuildOrderSerializer.Load).ToArray();

    public WebBuildOrderList List { get; } = new("Prueba", All.Select(bo =>
    {
        var s = BuildOrderSummary.Of(bo);
        return new WebBuildOrderEntry(bo.Id, s.Name, s.Civ, s.Map, s.Style, s.Tags, "Prueba", "Prueba", true, "public", DateTimeOffset.UnixEpoch, null, null);
    }).ToList());

    public BuildOrder? Saved(string code) => All.FirstOrDefault(b => b.Id == code);
    public IEnumerable<BuildOrder> AllSaved() => All;
    public Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default) => Task.FromResult(Saved(code)!);
}
