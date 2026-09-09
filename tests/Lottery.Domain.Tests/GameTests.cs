using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// Every per-game fact - draw days, draw time, the special ball's name - is a
/// switch over the enum. These pin the values callers depend on and, more
/// importantly, that a value outside the enum is rejected instead of quietly
/// taking one game's answer for another's.
/// </summary>
public class GameTests
{
    [Theory]
    [InlineData(Game.Powerball, "Powerball")]
    [InlineData(Game.MegaMillions, "Mega Ball")]
    public void SpecialBallName_IsTheGamesOwnLabel(Game game, string expected)
    {
        // User-facing: it names the ball in ticket-validation error messages.
        Assert.Equal(expected, game.SpecialBallName());
    }

    [Fact]
    public void AGameOutsideTheEnum_FailsLoudlyInsteadOfDefaulting()
    {
        // A third game added to the enum but not to these switches must throw
        // here rather than silently drawing on Powerball's schedule, and a
        // corrupt value crossing the API boundary must not resolve to a game.
        var unknown = (Game)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.DrawDays());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.DrawTimeEastern());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.SpecialBallName());
    }
}
