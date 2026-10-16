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
    public void La_cantidad_cambia_solo_si_el_numero_nuevo_se_sostiene()
    {
        var gate = new SuggestionGate(10, 20);
        gate.Update(0, WoodToFood with { Count = 3 });
        gate.Update(10, WoodToFood with { Count = 3 });

        // El asignador duda entre 2, 5 y 3: se sigue mostrando 3.
        Assert.Equal(3, gate.Update(11, WoodToFood with { Count = 2 })!.Count);
        Assert.Equal(3, gate.Update(12, WoodToFood with { Count = 5 })!.Count);
        Assert.Equal(3, gate.Update(13, WoodToFood with { Count = 3 })!.Count);
        // Un 4 que se sostiene 6 s sí se muestra.
        Assert.Equal(3, gate.Update(14, WoodToFood with { Count = 4 })!.Count);
        Assert.Equal(3, gate.Update(19, WoodToFood with { Count = 4 })!.Count);
        Assert.Equal(4, gate.Update(20, WoodToFood with { Count = 4 })!.Count);
    }

    [Fact]
    public void Una_vez_visible_dura_un_minimo_aunque_el_deficit_se_corte()
    {
        var gate = new SuggestionGate(10, 20, minShowSeconds: 8);
        gate.Update(0, WoodToFood);
        gate.Update(10, WoodToFood);

        Assert.Equal(WoodToFood, gate.Update(12, null));
        Assert.Equal(WoodToFood, gate.Update(17.5, null));
        Assert.Null(gate.Update(18, null));
    }
}
