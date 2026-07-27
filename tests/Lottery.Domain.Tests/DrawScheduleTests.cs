using Lottery.Domain;

namespace Lottery.Domain.Tests;

public class DrawScheduleTests
{
    // 2026-07-27 is a Monday (a Powerball draw day).
    private static readonly DateTimeOffset MondayNoonEt = new(2026, 7, 27, 16, 0, 0, TimeSpan.Zero); // 12:00 ET (EDT = UTC-4)

    [Fact]
    public void NextDraw_OnDrawDay_BeforeDrawTime_IsSameDay()
    {
        var next = DrawSchedule.NextDrawUtc(Game.Powerball, MondayNoonEt);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 2, 59, 0, TimeSpan.Zero), next); // Mon 22:59 EDT = Tue 02:59 UTC
    }

    [Fact]
    public void NextDraw_OnDrawDay_AfterDrawTime_RollsToNextDrawDay()
    {
        var justAfter = new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero); // Mon 23:00 EDT
        var next = DrawSchedule.NextDrawUtc(Game.Powerball, justAfter);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 2, 59, 0, TimeSpan.Zero), next); // Wed draw
    }

    [Fact]
    public void NextDraw_MegaMillions_FromMonday_IsTuesday()
    {
        var next = DrawSchedule.NextDrawUtc(Game.MegaMillions, MondayNoonEt);
        Assert.Equal(new DateTimeOffset(2026, 7, 29, 3, 0, 0, TimeSpan.Zero), next); // Tue 23:00 EDT
    }

    [Fact]
    public void NextDraw_AcrossDstEnd_UsesCorrectOffset()
    {
        // US DST ends Sun 2026-11-01. Saturday Oct 31 draw is EDT (UTC-4);
        // Monday Nov 2 draw is EST (UTC-5).
        var beforeSat = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var satDraw = DrawSchedule.NextDrawUtc(Game.Powerball, beforeSat);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 2, 59, 0, TimeSpan.Zero), satDraw);

        var afterSat = satDraw.AddMinutes(1);
        var monDraw = DrawSchedule.NextDrawUtc(Game.Powerball, afterSat);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 3, 59, 0, TimeSpan.Zero), monDraw); // 22:59 EST = 03:59 UTC
    }

    [Fact]
    public void PreviousDrawDate_BetweenDraws_IsMostRecentDrawDay()
    {
        var prev = DrawSchedule.PreviousDrawDate(Game.Powerball, MondayNoonEt);
        Assert.Equal(new DateOnly(2026, 7, 25), prev); // Saturday
    }

    [Fact]
    public void PreviousDrawDate_JustAfterDraw_IsToday()
    {
        var justAfter = new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero); // Mon 23:00 EDT
        var prev = DrawSchedule.PreviousDrawDate(Game.Powerball, justAfter);
        Assert.Equal(new DateOnly(2026, 7, 27), prev);
    }
}
