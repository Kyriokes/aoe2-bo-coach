namespace BoCoach.App.Tests;

/// <summary>Modo desarrollador (spec 2026-10-30, app para el público, parte 2).</summary>
public sealed class DevModeTests
{
    [Fact]
    public void Cinco_clics_rapidos_prenden_y_cuatro_no()
    {
        var clicks = new VersionClicks();
        var t = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.False(clicks.Click(t));
        Assert.False(clicks.Click(t.AddSeconds(0.5)));
        Assert.False(clicks.Click(t.AddSeconds(1)));
        Assert.False(clicks.Click(t.AddSeconds(1.5)));
        Assert.True(clicks.Click(t.AddSeconds(2)));
        Assert.False(clicks.Click(t.AddSeconds(2.2))); // vuelve a contar
    }

    [Fact]
    public void Clics_lentos_no_prenden()
    {
        var clicks = new VersionClicks();
        var t = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.All(Enumerable.Range(0, 10), i => Assert.False(clicks.Click(t.AddSeconds(i))));
    }
}
