using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D3: ramas de la BO disparadas por las señales de la partida.</summary>
public class BranchesTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    private static BuildOrder WithBranch(int? before = null) => Scouts with
    {
        Branches =
        [
            new Branch
            {
                On = "rival_feudal_click",
                Before = before,
                Note = "Lanceros contra sus exploradores",
                Steps = [new Step { Id = "r1", Do = StepAction.Train, What = "spearman", Count = 3 }],
            },
        ],
    };

    private static GameSnapshot At(double time, PlayerScore? score = null, params string[] events) =>
        new(time, PlanStart.Standard.Stock, 20, 25, events.Select(e => new GameEvent(e, time)).ToArray())
        {
            Scores = score is null ? null : [score],
        };

    /// <summary>El rival (rojo) hace clic en Feudal a los 502 s: su puntaje cae de 831 a 797 y queda abajo.</summary>
    private static CoachView RivalClicksFeudal(Coach coach, params string[] eventsBefore)
    {
        coach.Update(At(496, null, eventsBefore));
        coach.Update(At(498, new PlayerScore(2, 828, true)));
        coach.Update(At(500, new PlayerScore(2, 831, true)));
        coach.Update(At(502, new PlayerScore(2, 797, true)));
        return coach.Update(At(504, new PlayerScore(2, 799, true)));
    }

    [Fact]
    public void Las_ramas_se_guardan_en_el_json()
    {
        var json = BuildOrderSerializer.Serialize(WithBranch(600));
        var back = Assert.Single(BuildOrderSerializer.Parse(json).Branches!);

        Assert.Equal(("rival_feudal_click", 600, "spearman"), (back.On, back.Before, back.Steps[0].What));
    }

    [Fact]
    public void El_validador_rechaza_una_senal_que_no_existe_y_pasos_que_no_existen()
    {
        var bo = Scouts with { Branches = [new Branch { On = "rival_sneezed", Steps = [] }, new Branch { On = "under_attack", Steps = [new Step { Id = "a1", Do = StepAction.Train, What = "dragon" }] }] };
        var errors = new BuildOrderValidator(TestData.GameData).Validate(bo).Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message).ToList();

        Assert.Contains(errors, e => e.Contains("'rival_sneezed'"));
        Assert.Contains(errors, e => e.Contains("dragon"));
        Assert.True(new BuildOrderValidator(TestData.GameData).Validate(WithBranch()).IsValid);
    }

    [Fact]
    public void Con_la_senal_la_rama_va_primero_y_termina_con_sus_pasos()
    {
        var coach = new Coach(WithBranch(), TestData.GameData, TestData.Events);
        var view = RivalClicksFeudal(coach);

        Assert.NotNull(view.Branch);
        var lines = CoachText.Lines(view, TestData.GameData);
        Assert.Equal("Si el rival hace clic en Feudal: Entrenar 3 lanceros", lines.Current);
        Assert.Equal("Lanceros contra sus exploradores", lines.Status);
        Assert.StartsWith("Después, la BO: ", lines.Next);

        coach.Update(At(530, null, "spearman_created"));
        coach.Update(At(560, null, "spearman_created"));
        view = coach.Update(At(590, null, "spearman_created"));
        Assert.Null(view.Branch);
    }

    [Fact]
    public void Lo_hecho_antes_de_la_senal_no_cuenta_para_la_rama()
    {
        var coach = new Coach(WithBranch(), TestData.GameData, TestData.Events);
        var view = RivalClicksFeudal(coach, "spearman_created", "spearman_created", "spearman_created");

        Assert.Equal(0, view.Branch!.Current!.Completed);
    }

    [Fact]
    public void Si_la_senal_llega_despues_del_limite_no_hay_rama()
    {
        var coach = new Coach(WithBranch(before: 480), TestData.GameData, TestData.Events);

        Assert.Null(RivalClicksFeudal(coach).Branch);
    }

    [Fact]
    public void Ya_esta_cierra_la_rama_y_vuelve_a_la_BO()
    {
        var coach = new Coach(WithBranch(), TestData.GameData, TestData.Events);
        RivalClicksFeudal(coach);
        var current = coach.Tracker.Current;

        var view = coach.Advance();
        Assert.Null(view.Branch);
        Assert.Equal(current, coach.Tracker.Current); // la BO no avanzó
    }
}
