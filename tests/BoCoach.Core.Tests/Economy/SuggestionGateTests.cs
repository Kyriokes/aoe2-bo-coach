using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Tests.Economy;

public class SuggestionGateTests
{
    private static readonly Suggestion WoodToFood = new(Resource.Wood, Resource.Food, 2);
    private static readonly Suggestion FoodToWood = new(Resource.Food, Resource.Wood, 1);

    [Fact]
    public void Muestra_recien_cuando_el_deficit_se_sostiene_10_segundos()
    {
        var gate = new SuggestionGate(sustainSeconds: 10, cooldownSeconds: 20);

        Assert.Null(gate.Update(0, WoodToFood));
        Assert.Null(gate.Update(9.5, WoodToFood));
        Assert.Equal(WoodToFood, gate.Update(10, WoodToFood));
    }

    [Fact]
    public void Si_el_deficit_desaparece_se_reinicia_la_espera()
    {
        var gate = new SuggestionGate(10, 20);
        gate.Update(0, WoodToFood);
        gate.Update(5, null);

        Assert.Null(gate.Update(12, WoodToFood));
        Assert.Equal(WoodToFood, gate.Update(22, WoodToFood));
    }

    [Fact]
    public void Respeta_el_tiempo_minimo_entre_sugerencias()
    {
        var gate = new SuggestionGate(10, 20);
        gate.Update(0, WoodToFood);
        Assert.NotNull(gate.Update(10, WoodToFood));

        gate.Update(11, FoodToWood);
        Assert.Null(gate.Update(25, FoodToWood)); // sostenido 14 s, pero pasaron 15 desde la anterior
        Assert.Equal(FoodToWood, gate.Update(30, FoodToWood));
    }

    [Fact]
    public void La_sugerencia_vigente_actualiza_la_cantidad()
    {
        var gate = new SuggestionGate(10, 20);
        gate.Update(0, WoodToFood);
        gate.Update(10, WoodToFood);

        var updated = gate.Update(11, WoodToFood with { Count = 3 });

        Assert.Equal(3, updated!.Count);
    }
}
