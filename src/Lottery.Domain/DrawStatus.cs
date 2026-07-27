namespace Lottery.Domain;

public enum DrawStatus
{
    /// <summary>The drawing has not happened yet.</summary>
    Scheduled,

    /// <summary>The drawing has occurred but results are not yet available from the feed.</summary>
    Pending,

    /// <summary>Winning numbers are stored.</summary>
    Published,
}
