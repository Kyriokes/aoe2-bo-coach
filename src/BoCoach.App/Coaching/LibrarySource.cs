using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.App.Coaching;

/// <summary>La biblioteca local como fuente de BO del coach, hasta que la reemplace la lista de la web (parte 4).</summary>
internal sealed class LibrarySource(BuildOrderLibrary library) : IBuildOrderSource
{
    private IEnumerable<BuildOrder> All => library.List().Select(e => e.BuildOrder).OfType<BuildOrder>();

    public WebBuildOrderList List => new("", All.Select(bo =>
    {
        var s = BuildOrderSummary.Of(bo);
        return new WebBuildOrderEntry(bo.Id, s.Name, s.Civ, s.Map, s.Style, s.Tags, "", "", true, "private", DateTimeOffset.MinValue, null, null);
    }).ToList());

    public BuildOrder? Saved(string code) => All.FirstOrDefault(b => b.Id == code);

    public IEnumerable<BuildOrder> AllSaved() => All;

    public Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default) =>
        Saved(code) is { } bo ? Task.FromResult(bo) : Task.FromException<BuildOrder>(new WebLibraryException($"No hay ninguna BO {code} en la biblioteca."));
}
