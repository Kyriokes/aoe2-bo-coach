using BoCoach.App.Overlay;
using BoCoach.Core.Engine;

namespace BoCoach.App.Tests;

/// <summary>La GD como diferencia (diseño mínimo del mod, pedido del usuario del 2026-11-01).</summary>
public class ResourceTargetViewModelTests
{
    [Theory]
    [InlineData(6, 6, "=6")]
    [InlineData(16, 14, "+2")]
    [InlineData(5, 10, "-5")]
    [InlineData(6.4, 5.6, "=6")] // se redondean los dos antes de restar
    public void La_diferencia_dice_cuantos_mover(double target, double actual, string expected) =>
        Assert.Equal(expected, ResourceTargetViewModel.DifferenceText(target, actual));

    [Fact]
    public void Sin_saber_cuantos_hay_va_el_objetivo_solo_y_sin_objetivo_nada()
    {
        Assert.Equal("6", ResourceTargetViewModel.DifferenceText(6, null));
        Assert.Null(ResourceTargetViewModel.DifferenceText(null, 4));
    }

    [Fact]
    public void Apply_deja_el_objetivo_el_color_y_la_diferencia()
    {
        var target = new ResourceTargetViewModel("wood");
        target.Apply(16, 14, estimated: false);

        Assert.Equal(("16", TargetMatch.Short, "+2", "14"), (target.Target, target.Match, target.Difference, target.Actual));
    }
}
