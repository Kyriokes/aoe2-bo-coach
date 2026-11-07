using System.IO;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Web;

namespace BoCoach.App.Web;

/// <summary>
/// <c>BoCoach.exe --publish &lt;carpeta o .json&gt; [--dry-run]</c>: publica en la web, con la cuenta de Ajustes &gt;
/// Avanzado &gt; "Dirección y token…", cada BO de la carpeta, o la reemplaza si ya se publicó desde esta PC (otra corrida no las duplica). Sirve
/// para subir de una vez las BO por civ (build_orders/civs). Con <c>--dry-run</c> solo dice qué publicaría. Escribe
/// un renglón por BO en la salida estándar (la app no tiene consola: se ve si se la redirige, por ejemplo con
/// <c>| Write-Output</c> en PowerShell).
/// </summary>
public static class BulkPublish
{
    /// <summary>0 si se publicaron todas, 1 si alguna falló o tiene errores, 2 si no hay cuenta o no hay BO.</summary>
    public static async Task<int> RunAsync(CoachData data, WebAccount account, string source, bool dryRun, TextWriter output)
    {
        var files = Directory.Exists(source)
            ? Directory.GetFiles(source, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : File.Exists(source) ? [source] : [];
        if (files.Length == 0)
        {
            output.WriteLine($"No hay BO para publicar en {source}.");
            return 2;
        }
        if (!dryRun && !account.Settings.HasToken)
        {
            // Con los nombres de la app (Texts), así no queda mandando a un botón que ya no existe.
            output.WriteLine($"No hay cuenta de la web: pegá el token de \"Mi cuenta\" en {Texts.SettingsTitle} > {Texts.TabAdvanced} > \"{Texts.WebAccountButton}\" de la app.");
            return 2;
        }

        // Los códigos de lo publicado desde esta PC viven en web.json, que se pierde al reinstalar: sin la lista de la web,
        // otra corrida publicaba todo de nuevo y duplicaba la biblioteca. Las propias se reconocen por civ y nombre.
        ILookup<(string Civ, string Name), string> own = Array.Empty<WebBuildOrderEntry>().ToLookup(e => (e.Civ, e.Name), e => e.Code);
        if (account.Settings.HasToken)
        {
            try
            {
                var list = await account.Client.MyBuildOrdersAsync(account.Settings.Token);
                own = list.Items.Where(e => e.Own).ToLookup(e => (e.Civ, e.Name.Trim()), e => e.Code);
            }
            catch (WebLibraryException e)
            {
                output.WriteLine($"No se pudo pedir la lista de tus BO a la web ({e.Message}): sin ella se duplicarían las ya publicadas.");
                return 2;
            }
        }
        else
            output.WriteLine("Sin cuenta no se sabe cuáles ya están publicadas: \"se publicaría nueva\" puede ser una que ya está.");

        var (published, replaced, failed) = (0, 0, 0);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            BuildOrder bo;
            try
            {
                bo = BuildOrderSerializer.Load(file);
            }
            catch (Exception e) when (e is BuildOrderFormatException or IOException)
            {
                output.WriteLine($"✗ {name}: no se pudo leer ({e.Message})");
                failed++;
                continue;
            }
            if (data.Validator.Validate(bo).Issues.FirstOrDefault(i => i.Severity == Severity.Error) is { } error)
            {
                output.WriteLine($"✗ {name}: tiene errores ({error.Message})");
                failed++;
                continue;
            }

            var code = account.Settings.Published.GetValueOrDefault(bo.Id);
            if (code is null)
            {
                var matches = own[(bo.Civ, bo.Name.Trim())].ToList();
                if (matches.Count > 1)
                {
                    output.WriteLine($"✗ {name}: hay {matches.Count} publicadas tuyas con la misma civ y el nombre \"{bo.Name}\" ({string.Join(", ", matches)}): borrá las de más en la web y probá de nuevo.");
                    failed++;
                    continue;
                }
                code = matches.SingleOrDefault();
                if (code is not null && !dryRun)
                    account.RememberPublished(bo.Id, code);
            }
            if (dryRun)
            {
                output.WriteLine($"· {name}: \"{bo.Name}\" {(code is null ? "se publicaría nueva" : $"reemplazaría {code}")}");
                continue;
            }
            try
            {
                var result = await account.PublishAsync(bo, data.Planner);
                if (result.Code == code)
                    replaced++;
                else
                    published++;
                output.WriteLine($"{(result.Code == code ? "↻" : "✓")} {name}: \"{bo.Name}\" {result.Url}");
            }
            catch (WebLibraryException e)
            {
                output.WriteLine($"✗ {name}: {e.Message}");
                failed++;
            }
        }
        output.WriteLine(dryRun
            ? $"{files.Length - failed} se pueden publicar, {failed} con problemas."
            : $"{published} nuevas, {replaced} reemplazadas, {failed} con problemas.");
        return failed == 0 ? 0 : 1;
    }
}
