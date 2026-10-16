using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Tests.Economy;

public class VillagerAllocatorTests
{
    private static readonly EconomySettings Settings = new();

    // Lo que junta un aldeano por segundo: comida 0,3, madera 0,35, piedra 0,3, oro 0,35 (unos 20 por minuto).
    private static readonly ResourceAmounts Rates = new(Food: 0.3, Wood: 0.35, Stone: 0.3, Gold: 0.35);

    private static Suggestion? Exact(ResourceAmounts deficit, ResourceAmounts villagers) =>
        VillagerAllocator.Exact(deficit, villagers, Rates, Settings);

    [Fact]
    public void Mueve_lo_justo_para_cubrir_lo_que_falta()
    {
        // Faltan 60 de comida (35 por encima del umbral): con 18 por aldeano en 60 s alcanzan 2.
        var suggestion = Exact(new ResourceAmounts(Food: 60, Wood: -600, Stone: -200, Gold: -100), new ResourceAmounts(10, 10, 0, 2));

        Assert.Equal(new Suggestion(Resource.Wood, Resource.Food, 2), suggestion);
    }

    [Fact]
    public void Con_mucho_faltante_mueve_hasta_el_maximo()
    {
        var suggestion = Exact(new ResourceAmounts(Food: 500, Wood: -900, Stone: -200, Gold: -100), new ResourceAmounts(10, 10, 0, 2));

        Assert.Equal(new Suggestion(Resource.Wood, Resource.Food, Settings.MaxMoves), suggestion);
    }

    [Fact]
    public void Si_nada_falta_no_sugiere_aunque_algo_sobre()
    {
        Assert.Null(Exact(new ResourceAmounts(Food: -50, Wood: -900, Stone: -200, Gold: -400), new ResourceAmounts(8, 10, 0, 4)));
    }

    [Fact]
    public void Saca_gente_de_donde_mas_sobra()
    {
        // Oro sobra un poco (dentro de la tolerancia) y madera mucho: la gente sale de la madera.
        var suggestion = Exact(new ResourceAmounts(Food: 100, Wood: -700, Stone: -200, Gold: -160), new ResourceAmounts(8, 8, 0, 8));

        Assert.Equal((Resource.Wood, Resource.Food), (suggestion!.From, suggestion.To));
    }

    [Fact]
    public void No_deja_justo_al_que_cede()
    {
        // La madera sobra 120: con un aldeano menos quedaría en -99, todavía sobra; con dos, -78; con tres, -57 (justo).
        var suggestion = Exact(new ResourceAmounts(Food: 400, Wood: -120, Stone: -200, Gold: 0), new ResourceAmounts(8, 8, 0, 0));

        Assert.Equal(new Suggestion(Resource.Wood, Resource.Food, 2), suggestion);
    }

    [Fact]
    public void Abrir_un_recurso_nuevo_cuesta_mas()
    {
        // Falta un poco de oro y no hay nadie en el oro: no alcanza para mandar gente (campamento, caminata).
        Assert.Null(Exact(new ResourceAmounts(Food: -600, Wood: -100, Stone: -200, Gold: 50), new ResourceAmounts(12, 6, 0, 0)));

        // Si ya hay mineros, el mismo faltante sí se cubre (con 2: uno solo deja 29, por encima del umbral).
        Assert.Equal(new Suggestion(Resource.Food, Resource.Gold, 2),
            Exact(new ResourceAmounts(Food: -600, Wood: -100, Stone: -200, Gold: 50), new ResourceAmounts(12, 6, 0, 3)));
    }

    [Fact]
    public void Sin_aldeanos_en_un_recurso_no_puede_sacar_de_ahi()
    {
        // La piedra "sobra" (200 en stock) pero no tiene a nadie.
        Assert.Null(Exact(new ResourceAmounts(Food: 300, Wood: 0, Stone: -800, Gold: 0), new ResourceAmounts(8, 8, 0, 0)));
    }

    [Fact]
    public void El_costo_cuenta_lo_que_falta_entero_y_lo_que_sobra_con_menos_peso()
    {
        // Comida: 100 - 25 de umbral. Madera: (500 - 150) × 0,2.
        Assert.Equal(75 + 70, VillagerAllocator.Cost(new ResourceAmounts(Food: 100, Wood: -500, Stone: 0, Gold: -100), Settings), 6);
    }
}
