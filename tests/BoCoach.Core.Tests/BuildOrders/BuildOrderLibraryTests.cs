using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Tests.BuildOrders;

public sealed class BuildOrderLibraryTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bocoach-lib-");
    private readonly BuildOrderLibrary _library;

    public BuildOrderLibraryTests()
    {
        _library = new BuildOrderLibrary(_dir.FullName);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private static BuildOrder Bo(string name, params string[] tags) => new() { Name = name, Tags = tags, Steps = [] };

    [Fact]
    public void Guardar_asigna_id_y_aparece_en_la_lista()
    {
        var saved = _library.Save(Bo("Hombres de armas"));

        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
        var entry = Assert.Single(_library.List());
        Assert.Equal("Hombres de armas", entry.Name);
        Assert.Equal(_library.PathFor(saved), entry.Path);
    }

    [Fact]
    public void Buscar_ignora_mayusculas_y_acentos_y_mira_los_tags()
    {
        _library.Save(Bo("Arqueros rápidos", "feudal"));
        _library.Save(Bo("Fast castle", "boom"));

        Assert.Equal("Arqueros rápidos", Assert.Single(_library.Search("RAPIDOS")).Name);
        Assert.Equal("Fast castle", Assert.Single(_library.Search("boom")).Name);
        Assert.Equal(2, _library.Search("  ").Count);
        Assert.Empty(_library.Search("arqueros boom"));
    }

    [Fact]
    public void Duplicar_crea_otra_BO_con_otro_id()
    {
        var original = _library.Save(Bo("Scouts"));

        var copy = _library.Duplicate(original);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Scouts (copia)", copy.Name);
        Assert.Equal(2, _library.List().Count);
    }

    [Fact]
    public void Importar_y_exportar()
    {
        var external = Path.Combine(_dir.FullName, "..", $"externa-{Guid.NewGuid():N}.json");
        BuildOrderLibrary.Export(Bo("De un amigo") with { Id = "abc" }, external);
        try
        {
            var first = _library.Import(external);
            var second = _library.Import(external); // mismo id: no pisa la anterior

            Assert.Equal("abc", first.Id);
            Assert.NotEqual("abc", second.Id);
            Assert.Equal(2, _library.List().Count);
        }
        finally
        {
            File.Delete(external);
        }
    }

    [Fact]
    public void Importar_un_archivo_roto_da_error_claro()
    {
        var broken = Path.Combine(_dir.FullName, "..", $"rota-{Guid.NewGuid():N}.json");
        File.WriteAllText(broken, "{ \"schema_version\": 1, \"steps\": [ { \"do\": \"volar\" } ] }");
        try
        {
            var e = Assert.Throws<BuildOrderFormatException>(() => _library.Import(broken));
            Assert.Contains("steps[0].do", e.Message);
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public void Un_archivo_roto_en_la_biblioteca_se_lista_con_su_error()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "rota.json"), "{ nada");

        var entry = Assert.Single(_library.List());

        Assert.Null(entry.BuildOrder);
        Assert.Contains("JSON inválido", entry.Error);
        Assert.Equal("rota", entry.Name);
    }

    [Fact]
    public void Descartar_mueve_a_la_papelera_sin_borrar()
    {
        var saved = _library.Save(Bo("Para tirar"));
        var path = _library.PathFor(saved);

        var moved = _library.MoveToTrash(path);

        Assert.Empty(_library.List());
        Assert.True(File.Exists(moved));
        Assert.Equal(BuildOrderLibrary.TrashFolder, new DirectoryInfo(Path.GetDirectoryName(moved)!).Name);
    }

    [Fact]
    public void Primer_uso_copia_los_ejemplos()
    {
        _library.SeedIfEmpty(TestData.ExampleBuildOrders);
        _library.SeedIfEmpty(TestData.ExampleBuildOrders); // la segunda vez no hace nada

        Assert.Equal(TestData.ExampleBuildOrders.Count(), _library.List().Count);
    }

    [Theory]
    [InlineData(new string[0], "s1")]
    [InlineData(new[] { "s1", "s2" }, "s3")]
    [InlineData(new[] { "s1", "s7", "x" }, "s8")]
    public void Siguiente_id_de_paso(string[] existing, string expected)
    {
        Assert.Equal(expected, StepIds.Next(existing));
    }
}
