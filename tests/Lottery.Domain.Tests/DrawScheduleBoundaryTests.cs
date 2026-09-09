using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// Boundary cases the base schedule tests leave open: the exact draw instant
/// (strictly-after vs at-or-before semantics), the spring DST transition (the
/// base suite covers only the fall one), and Mega Millions' previous-draw math.
/// </summary>
public class DrawScheduleBoundaryTests
{
    // Monday 2026-07-27 22:59 EDT == Tuesday 2026-07-28 02:59 UTC.
    private static readonly DateTimeOffset MondayDrawInstantUtc = new(2026, 7, 28, 2, 59, 0, TimeSpan.Zero);

    [Fact]
    public void NextDraw_AtTheExactDrawInstant_RollsToTheFollowingDrawDay()
    {
        // "Strictly after now": at 22:59:00 the drawing is happening, not upcoming.
        var next = DrawSchedule.NextDrawUtc(Game.Powerball, MondayDrawInstantUtc);

        Assert.Equal(new DateTimeOffset(2026, 7, 30, 2, 59, 0, TimeSpan.Zero), next); // Wednesday's draw
    }

    [Fact]
    public void PreviousDrawDate_AtTheExactDrawInstant_IsThatSameDay()
    {
        // "At or before now": the moment the drawing starts it counts as held.
        var previous = DrawSchedule.PreviousDrawDate(Game.Powerball, MondayDrawInstantUtc);

        Assert.Equal(new DateOnly(2026, 7, 27), previous);
    }

    [Fact]
    public void NextDraw_AcrossDstStart_UsesCorrectOffset()
    {
        // US DST starts Sun 2026-03-08. Saturday Mar 7 draw is EST (UTC-5);
        // Monday Mar 9 draw is EDT (UTC-4).
        var beforeSaturday = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
        var saturdayDraw = DrawSchedule.NextDrawUtc(Game.Powerball, beforeSaturday);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 59, 0, TimeSpan.Zero), saturdayDraw); // 22:59 EST

        var afterSaturday = saturdayDraw.AddMinutes(1);
        var mondayDraw = DrawSchedule.NextDrawUtc(Game.Powerball, afterSaturday);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 2, 59, 0, TimeSpan.Zero), mondayDraw); // 22:59 EDT
    }

    [Fact]
    public void DrawInstantUtc_WinterAndSummer_DifferByTheDstHour()
    {
        // Same 22:59 Eastern wall clock, one hour apart in UTC.
        var winter = DrawSchedule.DrawInstantUtc(Game.Powerball, new DateOnly(2026, 1, 5));  // a Monday, EST
        var summer = DrawSchedule.DrawInstantUtc(Game.Powerball, new DateOnly(2026, 7, 27)); // a Monday, EDT

        Assert.Equal(new DateTimeOffset(2026, 1, 6, 3, 59, 0, TimeSpan.Zero), winter);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 2, 59, 0, TimeSpan.Zero), summer);
    }

    [Fact]
    public void DrawInstantUtc_MegaMillions_Uses2300Eastern()
    {
        var instant = DrawSchedule.DrawInstantUtc(Game.MegaMillions, new DateOnly(2026, 7, 24)); // a Friday, EDT

        Assert.Equal(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero), instant);
    }

    [Fact]
    public void PreviousDrawDate_MegaMillions_FromMondayNoon_IsFriday()
    {
        var mondayNoonEt = new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero);

        var previous = DrawSchedule.PreviousDrawDate(Game.MegaMillions, mondayNoonEt);

        Assert.Equal(new DateOnly(2026, 7, 24), previous);
    }

    [Fact]
    public void NextDraw_MegaMillions_JustAfterFridayDraw_IsTuesday()
    {
        var justAfterFriday = new DateTimeOffset(2026, 7, 25, 3, 30, 0, TimeSpan.Zero); // Fri 23:30 EDT

        var next = DrawSchedule.NextDrawUtc(Game.MegaMillions, justAfterFriday);

        Assert.Equal(new DateTimeOffset(2026, 7, 29, 3, 0, 0, TimeSpan.Zero), next); // Tue 23:00 EDT
    }
}
