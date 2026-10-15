using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

public class BuildOrderValidatorTests
{
    private static readonly BuildOrderValidator Validator = new(TestData.GameData, TestData.Events);

    private static BuildOrder Bo(params Step[] steps) => new() { Id = "t", Name = "Prueba", Steps = steps };

    private static Step Villagers(string id, int count = 1, GatherSource? assign = GatherSource.Sheep) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = assign };

    private static Step Build(string id, string what) => new() { Id = id, Do = StepAction.Build, What = what };

    private static Step Research(string id, string what) => new() { Id = id, Do = StepAction.Research, What = what };

    private static ValidationIssue SingleError(BuildOrder bo)
    {
        var result = Validator.Validate(bo);
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors);
    }

    [Theory]
    [MemberData(nameof(Ejemplos))]
    public void Las_BO_de_ejemplo_validan_sin_errores_ni_avisos(string path)
    {
        var result = Validator.Validate(BuildOrderSerializer.Load(path));

        Assert.True(result.IsValid, string.Join("\n", result.Issues));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Aldeanos_sin_recurso_asignado()
    {
        var error = SingleError(Bo(Villagers("s1", assign: null)));

        Assert.Equal("s1", error.StepId);
        Assert.Contains("assign", error.Message);
    }

    [Fact]
    public void Entidad_que_no_existe()
    {
        var error = SingleError(Bo(Build("s1", "pizzeria")));

        Assert.Contains("'pizzeria' no existe", error.Message);
    }

    [Fact]
    public void Accion_que_no_corresponde_al_tipo_de_entidad()
    {
        var error = SingleError(Bo(new Step { Id = "s1", Do = StepAction.Train, What = "house" }));

        Assert.Contains("No se puede entrenar 'house': es un edificio", error.Message);
    }

    [Fact]
    public void Entidad_de_una_edad_que_todavia_no_se_alcanzo()
    {
        var error = SingleError(Bo(Build("s1", "archery_range")));

        Assert.Contains("Edad Feudal", error.Message);
        Assert.Contains("Edad Oscura", error.Message);
    }

    [Fact]
    public void Despues_de_investigar_la_edad_ya_se_puede()
    {
        var result = Validator.Validate(Bo(
            Build("s1", "barracks"), Build("s2", "mill"), Research("s3", "feudal_age"), Build("s4", "archery_range")));

        Assert.True(result.IsValid, string.Join("\n", result.Issues));
    }

    [Fact]
    public void Dependencia_circular()
    {
        var bo = Bo(
            Villagers("s1") with { After = ["s3"] },
            Villagers("s2") with { After = ["s1"] },
            Villagers("s3") with { After = ["s2"] });

        var error = SingleError(bo);

        Assert.Contains("circular", error.Message);
        Assert.Contains("s1 → s3 → s2 → s1", error.Message);
    }

    [Fact]
    public void Paso_que_depende_de_si_mismo()
    {
        var error = SingleError(Bo(Villagers("s1") with { After = ["s1"] }));

        Assert.Contains("sí mismo", error.Message);
    }

    [Fact]
    public void After_a_un_paso_inexistente()
    {
        var error = SingleError(Bo(Villagers("s1") with { After = ["s9"] }));

        Assert.Contains("'s9'", error.Message);
    }

    [Fact]
    public void Ids_repetidos()
    {
        var error = SingleError(Bo(Villagers("s1"), Villagers("s1")));

        Assert.Contains("repetido", error.Message);
    }

    [Fact]
    public void Evento_de_confirmacion_desconocido()
    {
        var error = SingleError(Bo(Build("s1", "house") with { ConfirmBy = "casa_lista" }));

        Assert.Contains("casa_lista", error.Message);
    }

    [Theory]
    [InlineData(null, GatherSource.Gold, "from")]
    [InlineData(GatherSource.Wood, null, "assign")]
    [InlineData(GatherSource.Wood, GatherSource.Wood, "mismo recurso")]
    public void Move_mal_armado(GatherSource? from, GatherSource? assign, string expected)
    {
        var error = SingleError(Bo(new Step { Id = "s1", Do = StepAction.Move, From = from, Assign = assign, Count = 2 }));

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData(0, null, "count")]
    [InlineData(1, 0, "at_pop")]
    [InlineData(1, 201, "at_pop")]
    public void Numeros_fuera_de_rango(int count, int? atPop, string expected)
    {
        var error = SingleError(Bo(Villagers("s1", count) with { AtPop = atPop }));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Assign_en_unidad_militar()
    {
        var bo = Bo(Build("s0", "barracks"), new Step { Id = "s1", Do = StepAction.Train, What = "militia", Assign = GatherSource.Wood });

        Assert.Contains("solo tiene sentido para aldeanos", SingleError(bo).Message);
    }

    [Fact]
    public void Aviso_si_falta_un_edificio_de_la_edad_para_subir()
    {
        var result = Validator.Validate(Bo(Build("s1", "mill"), Research("s2", "feudal_age")));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("s2", warning.StepId);
        Assert.Contains("2 edificios", warning.Message);
    }

    [Fact]
    public void Aviso_si_se_produce_en_un_edificio_que_no_se_construyo()
    {
        var result = Validator.Validate(Bo(new Step { Id = "s1", Do = StepAction.Train, What = "militia" }));

        Assert.True(result.IsValid);
        Assert.Contains("barracks", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void BO_vacia_y_sin_nombre()
    {
        var result = Validator.Validate(new BuildOrder());

        Assert.Equal(2, result.Errors.Count());
    }

    [Fact]
    public void Objetivos_incoherentes()
    {
        var bo = Bo(Villagers("s1")) with
        {
            Targets = new Targets { FeudalClickTime = 600, CastleClickTime = 500, VillagersAtFeudal = new(-1, 0, 0, 0) },
        };

        var errors = Validator.Validate(bo).Errors.Select(e => e.Message).ToList();

        Assert.Contains(errors, m => m.Contains("castle_click_time"));
        Assert.Contains(errors, m => m.Contains("negativos"));
    }

    [Fact]
    public void El_mensaje_completo_nombra_paso_y_severidad()
    {
        var error = SingleError(Bo(Villagers("s7", assign: null)));

        Assert.StartsWith("Error en el paso s7:", error.ToString());
    }

    public static TheoryData<string> Ejemplos() => new(TestData.ExampleBuildOrders);
}
