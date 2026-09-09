using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// The draw calendar and times are reference facts the whole schedule builds
/// on; a wrong day here silently shifts every countdown and Pending window.
/// </summary>
public class GameExtensionsTests
{
    [Fact]
    public void Powerball_DrawsMondayWednesdaySaturday()
    {
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday],
            Game.Powerball.DrawDays());
    }

    [Fact]
    public void MegaMillions_DrawsTuesdayFriday()
    {
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Friday],
            Game.MegaMillions.DrawDays());
    }

    [Fact]
    public void Powerball_DrawsAt2259Eastern()
    {
        Assert.Equal(new TimeOnly(22, 59), Game.Powerball.DrawTimeEastern());
    }

    [Fact]
    public void MegaMillions_DrawsAt2300Eastern()
    {
        Assert.Equal(new TimeOnly(23, 0), Game.MegaMillions.DrawTimeEastern());
    }
}
