using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.Data;

/// <summary>D10: las estadísticas reales de cruces de data/matchup_stats.json (las escribe tools/stats).</summary>
public class MatchupStatsTests
{
    private static readonly Dictionary<string, Func<int, int, int, int[]>> UnaPareja = new()
    {
        ["britons|franks"] = MatchupJson.Same(497, 469, 525, 93),
    };

    private static readonly Dictionary<string, Func<int, int, int, int[]>> UnaCiv = new()
    {
        ["franks"] = MatchupJson.Same(510, 500, 520, 900),
    };

    [Fact]
    public void Lee_la_cabecera_y_las_franjas()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(UnaPareja, UnaCiv, previousMixed: true));

        Assert.False(stats.IsEmpty);
        Assert.Equal("aoe2-guide", stats.SourceName);
        Assert.Equal("Update 185872", stats.PatchLabel);
        Assert.Equal(new DateOnly(2026, 6, 1), stats.From);
        Assert.Equal(new DateOnly(2026, 7, 3), stats.To);
        Assert.True(stats.PreviousMixed);
        Assert.Equal([850, 1050, 1300, 1600], stats.EloLimits);
        Assert.Equal([20, 30, 40, 55], stats.GameMinuteLimits);
        Assert.Equal(["arabia", "arena", "black_forest", "nomad"], stats.MapKeys);
    }

    [Fact]
    public void Franjas_con_los_bordes()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(UnaPareja, UnaCiv));

        Assert.Equal(0, stats.EloBand(null));
        Assert.Equal(0, stats.EloBand(0));
        Assert.Equal(1, stats.EloBand(849));
        Assert.Equal(2, stats.EloBand(850));
        Assert.Equal(5, stats.EloBand(1600));
        Assert.Equal(1, stats.DurationBand(new TimeSpan(0, 19, 59)));
        Assert.Equal(2, stats.DurationBand(TimeSpan.FromMinutes(20)));
        Assert.Equal(5, stats.DurationBand(TimeSpan.FromMinutes(55)));
        Assert.Equal(1, stats.MapIndex("arabia"));
        Assert.Equal(4, stats.MapIndex("nomad"));
        Assert.Equal(4, stats.MapIndex(" Nomad "));
        Assert.Equal(0, stats.MapIndex("megarandom"));
        Assert.Equal(0, stats.MapIndex(null));
        Assert.Equal("antes de los 20 min", stats.DurationLabel(1));
        Assert.Equal("de 30 a 40 min", stats.DurationLabel(3));
        Assert.Equal("después de los 55 min", stats.DurationLabel(5));
    }

    [Fact]
    public void Un_archivo_roto_o_que_falta_da_vacio()
    {
        Assert.True(MatchupStats.Load(Path.Combine(AppContext.BaseDirectory, "no_existe.json")).IsEmpty);
        Assert.True(MatchupStats.Parse("").IsEmpty);
        Assert.True(MatchupStats.Parse("{").IsEmpty);
        Assert.True(MatchupStats.Parse("""{ "one_vs_one": 3, "team": "x" }""").IsEmpty);
        Assert.True(MatchupStats.Parse("[1, 2]").IsEmpty);
        Assert.True(MatchupStats.Empty.IsEmpty);
    }
}
