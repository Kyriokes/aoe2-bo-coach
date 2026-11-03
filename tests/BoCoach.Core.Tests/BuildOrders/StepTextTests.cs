using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

public class StepTextTests
{
    [Fact]
    public void Describe_cada_tipo_de_paso()
    {
        Assert.Equal("6 aldeanos en ovejas",
            StepText.Describe(new Step { Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep }));
        Assert.Equal("Construir 2 casas (3 constructores) → ovejas",
            StepText.Describe(new Step { Do = StepAction.Build, What = "house", Count = 2, Builders = 3, Assign = GatherSource.Sheep }));
        Assert.Equal("Construir 1 campamento maderero → madera",
            StepText.Describe(new Step { Do = StepAction.Build, What = "lumber_camp", Assign = GatherSource.Wood }));
        Assert.Equal("Investigar Edad Feudal a 22 de población",
            StepText.Describe(new Step { Do = StepAction.Research, What = "feudal_age", AtPop = 22 }));
        Assert.Equal("Mover 3 de madera a oro",
            StepText.Describe(new Step { Do = StepAction.Move, Count = 3, From = GatherSource.Wood, Assign = GatherSource.Gold }));
    }

    [Fact]
    public void Describe_cada_tipo_de_paso_en_ingles()
    {
        Assert.Equal("6 villagers on sheep",
            StepText.Describe(new Step { Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep }, StepText.English));
        Assert.Equal("Build 2 houses (3 builders) → sheep",
            StepText.Describe(new Step { Do = StepAction.Build, What = "house", Count = 2, Builders = 3, Assign = GatherSource.Sheep }, StepText.English));
        Assert.Equal("Build 1 lumber camp → wood",
            StepText.Describe(new Step { Do = StepAction.Build, What = "lumber_camp", Assign = GatherSource.Wood }, StepText.English));
        Assert.Equal("Research Feudal Age at 22 pop",
            StepText.Describe(new Step { Do = StepAction.Research, What = "feudal_age", AtPop = 22 }, StepText.English));
        Assert.Equal("Move 3 from wood to gold",
            StepText.Describe(new Step { Do = StepAction.Move, Count = 3, From = GatherSource.Wood, Assign = GatherSource.Gold }, StepText.English));
        Assert.Equal("Send 1 idle villager to berries",
            StepText.Describe(new Step { Do = StepAction.Move, Count = 1, Assign = GatherSource.Berries }, StepText.English));
        Assert.Equal("Train 5 archers",
            StepText.Describe(new Step { Do = StepAction.Train, What = "archer", Count = 5 }, StepText.English));
    }

    [Fact]
    public void Un_idioma_desconocido_sale_en_castellano()
    {
        Assert.Equal("Mover 3 de madera a oro",
            StepText.Describe(new Step { Do = StepAction.Move, Count = 3, From = GatherSource.Wood, Assign = GatherSource.Gold }, "fr"));
    }

    [Theory]
    [InlineData(StepText.Spanish)]
    [InlineData(StepText.English)]
    public void Todas_las_entidades_de_game_data_tienen_nombre(string language)
    {
        Assert.All(TestData.GameData.Entities.Keys, key => Assert.True(StepText.HasName(key, language), $"falta el nombre de '{key}' en {language}"));
    }

    [Fact]
    public void Las_senales_de_rama_tienen_texto_en_los_dos_idiomas()
    {
        Assert.All(Branch.Signals.Keys, key => Assert.NotEqual(key, Branch.SignalText(key, StepText.English)));
        Assert.Equal("Si te atacan", Branch.SignalText("under_attack"));
        Assert.Equal("otra", Branch.SignalText("otra", StepText.English));
    }
}
