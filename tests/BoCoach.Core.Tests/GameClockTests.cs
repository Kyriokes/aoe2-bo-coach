namespace BoCoach.Core.Tests;

public class GameClockTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(540, "9:00")]
    [InlineData(599.6, "10:00")]
    [InlineData(3750, "1:02:30")]
    [InlineData(-5, "0:00")]
    public void Formato(double seconds, string expected) => Assert.Equal(expected, GameClock.Format(seconds));

    [Theory]
    [InlineData("9:00", 540)]
    [InlineData(" 9:5 ", 545)]
    [InlineData("540", 540)]
    [InlineData("1:02:30", 3750)]
    public void Lee_tiempos_validos(string text, int expected)
    {
        Assert.True(GameClock.TryParse(text, out var seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nueve")]
    [InlineData("9:-1")]
    [InlineData("1:2:3:4")]
    [InlineData(null)]
    public void Rechaza_tiempos_invalidos(string? text) => Assert.False(GameClock.TryParse(text, out _));
}
