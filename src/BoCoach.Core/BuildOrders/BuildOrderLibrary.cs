using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BoCoach.Core.BuildOrders;

/// <summary>Un archivo de la biblioteca: la BO si se pudo leer, o el error si está roto.</summary>
public sealed record LibraryEntry(string Path, BuildOrder? BuildOrder, string? Error)
{
    public string Name => BuildOrder?.Name is { Length: > 0 } name ? name : System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Biblioteca local de build orders: una carpeta con un archivo .json por BO, nombrado por su id.
/// Nunca borra: lo que se descarta va a la subcarpeta "papelera".
/// </summary>
public sealed partial class BuildOrderLibrary
{
    public const string TrashFolder = "papelera";

    public BuildOrderLibrary(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public IReadOnlyList<LibraryEntry> List() => System.IO.Directory
        .EnumerateFiles(Directory, "*.json")
        .Select(Read)
        .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Busca en nombre, civ, tags, autor y descripción, sin distinguir mayúsculas ni acentos.</summary>
    public IReadOnlyList<LibraryEntry> Search(string? text)
    {
        var all = List();
        var words = Normalize(text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return all;
        return all.Where(e =>
        {
            var bo = e.BuildOrder;
            var haystack = Normalize(string.Join(' ', [e.Name, bo?.Civ, bo?.Author, bo?.Description, .. bo?.Tags ?? []]));
            return words.All(haystack.Contains);
        }).ToList();
    }

    public string PathFor(BuildOrder bo) => System.IO.Path.Combine(Directory, $"{SafeFileName(bo.Id)}.json");

    /// <summary>Guarda la BO (le asigna un id si no tiene) y devuelve la BO guardada.</summary>
    public BuildOrder Save(BuildOrder bo)
    {
        if (string.IsNullOrWhiteSpace(bo.Id))
            bo = bo with { Id = NewId() };
        BuildOrderSerializer.Save(bo, PathFor(bo));
        return bo;
    }

    public BuildOrder Duplicate(BuildOrder bo) => Save(bo with { Id = NewId(), Name = $"{bo.Name} (copia)" });

    /// <summary>Copia una BO de afuera a la biblioteca. Si su id ya existe, le da uno nuevo.</summary>
    public BuildOrder Import(string sourcePath) => Add(BuildOrderSerializer.Load(sourcePath));

    /// <summary>
    /// Agrega una BO que vino de afuera (archivo, portapapeles o web); si su id falta, ya está en uso o no sirve como
    /// nombre de archivo, le da uno nuevo.
    /// </summary>
    public BuildOrder Add(BuildOrder bo)
    {
        if (!IsFileNameId(bo.Id) || File.Exists(PathFor(bo)))
            bo = bo with { Id = NewId() };
        return Save(bo);
    }

    /// <summary>
    /// El id es el nombre del archivo y lo de afuera trae cualquiera: se acepta solo letras, números, '.', '_' y '-'
    /// (hasta 40 caracteres), y nunca un dispositivo de Windows ("CON", "nul.txt", "COM1": el archivo no se crea).
    /// </summary>
    public static bool IsFileNameId(string? id) =>
        id is not null && FileNameId().IsMatch(id) && !ReservedNames.Contains(id.Split('.')[0]);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$")]
    private static partial Regex FileNameId();

    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })],
        StringComparer.OrdinalIgnoreCase);

    public static void Export(BuildOrder bo, string destinationPath) => BuildOrderSerializer.Save(bo, destinationPath);

    /// <summary>Mueve el archivo a la papelera de la biblioteca (no lo borra).</summary>
    public string MoveToTrash(string path)
    {
        var trash = System.IO.Path.Combine(Directory, TrashFolder);
        System.IO.Directory.CreateDirectory(trash);
        var destination = System.IO.Path.Combine(trash, System.IO.Path.GetFileName(path));
        if (File.Exists(destination))
        {
            destination = System.IO.Path.Combine(trash,
                $"{System.IO.Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        }
        File.Move(path, destination);
        return destination;
    }

    /// <summary>Copia las BO de ejemplo si la biblioteca está vacía (primer uso).</summary>
    public void SeedIfEmpty(IEnumerable<string> examplePaths)
    {
        if (System.IO.Directory.EnumerateFiles(Directory, "*.json").Any())
            return;
        foreach (var path in examplePaths)
            File.Copy(path, System.IO.Path.Combine(Directory, System.IO.Path.GetFileName(path)));
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    private static LibraryEntry Read(string path)
    {
        try
        {
            return new LibraryEntry(path, BuildOrderSerializer.Load(path), null);
        }
        catch (Exception e) when (e is BuildOrderFormatException or IOException)
        {
            return new LibraryEntry(path, null, e.Message);
        }
    }

    private static string SafeFileName(string id)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Ids de pasos: "s1", "s2", ... El siguiente es uno más que el mayor número usado.</summary>
public static class StepIds
{
    public static string Next(IEnumerable<string> existing)
    {
        var max = existing
            .Select(id => id.Length > 1 && id[0] == 's' && int.TryParse(id.AsSpan(1), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"s{max + 1}";
    }
}
