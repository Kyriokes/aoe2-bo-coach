using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

public class StepTextTests
{
    [Fact]
    public void Describe_cada_tipo_de_paso()
    {
        Assert.Equal("Entrenar 6 aldeanos → ovejas",
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
    public void Todas_las_entidades_de_game_data_tienen_nombre_en_espanol()
    {
        Assert.All(TestData.GameData.Entities.Keys, key => Assert.True(StepText.HasName(key), $"falta el nombre de '{key}'"));
    }
}
