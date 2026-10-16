using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;
using BoCoach.Core.Engine;
using BoCoach.Core.Tests.Engine;

namespace BoCoach.Core.Tests.Economy;

public class EconomyAdvisorTests
{
    // Tasa por aldeano que asume el Planner (tasa del .dat × eficiencia por defecto).
    private const double FoodRate = 0.33 * 0.95;
    private const double WoodRate = 0.39 * 0.8;

    private static readonly BuildOrder Bo = new()
    {
        Name = "Economía de prueba",
        Steps =
        [
            new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep },
            new Step { Id = "s2", Do = StepAction.Train, What = "villager", Count = 8, Assign = GatherSource.Wood },
            new Step { Id = "s3", Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep },
            new Step { Id = "s4", Do = StepAction.Research, What = "feudal_age" },
        ],
        Targets = new Targets { FeudalClickTime = 540 },
    };

    /// <summary>
    /// Partida simulada: el jugador ya entrenó los 14 aldeanos de s1 y s2, pero en realidad tiene
    /// <paramref name="food"/> en comida y <paramref name="wood"/> en madera. Avanza de a 0,5 s y sigue las sugerencias.
    /// </summary>
    private sealed class Escenario
    {
        private readonly Partida _partida = new();
        private readonly Tracker _tracker;
        private readonly EconomyAdvisor _advisor;
        private double _food;
        private double _wood;

        public Escenario(int food, int wood, double start = 300, AllocatorKind allocator = AllocatorKind.Exact)
        {
            _advisor = new EconomyAdvisor(TestData.GameData, settings: new EconomySettings { Allocator = allocator });
            _food = food;
            _wood = wood;
            _partida.Play(Bo, 0, 1);
            _tracker = new Tracker(Bo, TestData.GameData);
            _partida.Stock = new ResourceAmounts(Food: 50, Wood: 400, Stone: 200, Gold: 100);
            _partida.Wait(start - _partida.Time);
        }

        public List<(double Time, Suggestion Suggestion)> Shown { get; } = [];
        public EconomyReport? Last { get; private set; }

        public void Run(double seconds, bool followSuggestions)
        {
            double? followAt = null;
            Suggestion? previous = null;
            for (var t = 0.0; t < seconds; t += 0.5)
            {
                _partida.Stock += new ResourceAmounts(Food: _food * FoodRate * 0.5, Wood: _wood * WoodRate * 0.5, Stone: 0, Gold: 0);
                _partida.Wait(0.5);
                _tracker.Update(_partida.State);
                Last = _advisor.Update(_tracker);

                // Una sugerencia "nueva" es la que aparece o cambia de par de recursos (no de cantidad).
                if (Last.Suggestion is { } s && (previous is null || (previous.From, previous.To) != (s.From, s.To)))
                {
                    Shown.Add((_partida.Time, s));
                    followAt = _partida.Time + 3; // el jugador tarda 3 s en hacerle caso
                }
                previous = Last.Suggestion;
                if (followSuggestions && followAt <= _partida.Time && Last.Suggestion is { } follow)
                {
                    Move(follow);
                    followAt = null;
                }
            }
        }

        private void Move(Suggestion s)
        {
            double Take(Resource r, double n)
            {
                if (r == Resource.Food) { var k = Math.Min(n, _food); _food -= k; return k; }
                if (r == Resource.Wood) { var k = Math.Min(n, _wood); _wood -= k; return k; }
                return 0;
            }
            var moved = Take(s.From, s.Count);
            if (s.To == Resource.Food) _food += moved;
            if (s.To == Resource.Wood) _wood += moved;
        }

        public (double Food, double Wood) Real => (_food, _wood);
    }

    [Theory]
    [InlineData(AllocatorKind.Exact)]
    [InlineData(AllocatorKind.Greedy)]
    public void Si_sobra_madera_sugiere_pasar_aldeanos_a_comida(AllocatorKind allocator)
    {
        var escenario = new Escenario(food: 3, wood: 14, allocator: allocator);

        escenario.Run(seconds: 40, followSuggestions: false);

        var (time, suggestion) = Assert.Single(escenario.Shown);
        Assert.Equal(Resource.Wood, suggestion.From);
        Assert.Equal(Resource.Food, suggestion.To);
        Assert.InRange(suggestion.Count, 1, 5);
        Assert.StartsWith("Pasá ", suggestion.ToString());
        Assert.EndsWith("de madera a comida", suggestion.ToString());
        Assert.True(time >= 300 + 10, "la histéresis pide 10 s sostenidos");
    }

    [Theory]
    [InlineData(AllocatorKind.Exact)]
    [InlineData(AllocatorKind.Greedy)]
    public void Sin_oscilaciones_en_5_minutos_siguiendo_las_sugerencias(AllocatorKind allocator)
    {
        var escenario = new Escenario(food: 3, wood: 14, allocator: allocator);

        escenario.Run(seconds: 300, followSuggestions: true);

        Assert.NotEmpty(escenario.Shown);
        Assert.All(escenario.Shown, s => Assert.Equal((Resource.Wood, Resource.Food), (s.Suggestion.From, s.Suggestion.To)));
        for (var i = 1; i < escenario.Shown.Count; i++)
            Assert.True(escenario.Shown[i].Time - escenario.Shown[i - 1].Time >= 20, "una sugerencia cada 20 s como mínimo");
        Assert.True(escenario.Real.Food > 3, "el jugador terminó con más gente en comida");
    }

    [Theory]
    [InlineData(AllocatorKind.Exact)]
    [InlineData(AllocatorKind.Greedy)]
    public void Con_la_economia_equilibrada_no_sugiere_nada(AllocatorKind allocator)
    {
        var escenario = new Escenario(food: 12, wood: 5, allocator: allocator);

        escenario.Run(seconds: 60, followSuggestions: false);

        Assert.Empty(escenario.Shown);
    }

    [Fact]
    public void Aprende_la_eficiencia_y_reparte_el_total_de_aldeanos()
    {
        var escenario = new Escenario(food: 3, wood: 14);

        escenario.Run(seconds: 30, followSuggestions: false);

        var villagers = escenario.Last!.Villagers;
        Assert.Equal(17, villagers.Total); // 18 de población menos el explorador
        Assert.InRange(villagers.Efficiency, 0.9, 1.1);
        Assert.Equal(17, villagers.PerResource.Food + villagers.PerResource.Wood + villagers.PerResource.Gold + villagers.PerResource.Stone, 3);
        Assert.True(villagers.PerResource.Wood > villagers.PerResource.Food);
    }

    [Fact]
    public void La_demanda_cubre_lo_que_falta_hasta_el_proximo_hito()
    {
        var escenario = new Escenario(food: 3, wood: 14);

        escenario.Run(seconds: 0.5, followSuggestions: false);

        // s3: 6 aldeanos (300 de comida) + Feudal (500) en los 239,5 s que faltan para el objetivo (9:00).
        Assert.Equal(800.0 / 239.5, escenario.Last!.Demand.Food, 2);
        Assert.Equal(0, escenario.Last.Demand.Wood);
    }
}
