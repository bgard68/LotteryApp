using System.Collections.Frozen;
using Lottery.Domain;

namespace Lottery.Infrastructure.Feeds;

/// <summary>
/// Per-game facts about the <em>sources</em> — dataset ids, payload shape, snapshot file names.
/// </summary>
/// <remarks>
/// <para>
/// The Infrastructure counterpart to <see cref="GameProfile"/>, kept separate on purpose: a
/// Socrata dataset id and the number of integers in a <c>winning_numbers</c> string are facts
/// about somebody else's HTTP API, not about the game, and the Domain should not learn them.
/// </para>
/// <para>
/// Same reason for existing, though. These were four more
/// <c>game == Game.Powerball ? x : y</c> expressions in three files, including the one that
/// decides whether the special ball is the sixth number in the array or a field of its own —
/// which means a third game would have been parsed with Mega Millions' layout against Mega
/// Millions' dataset, and the first sign of it would have been wrong numbers on the site.
/// </para>
/// </remarks>
internal sealed record GameFeedProfile(
    Game Game,
    string SocrataDataset,
    int WinningNumbersCount,
    bool SpecialIsLastWinningNumber,
    string SnapshotResource);

internal static class GameFeedProfiles
{
    private static readonly IReadOnlyList<GameFeedProfile> All =
    [
        new(
            Game.Powerball,
            SocrataDataset: "d6yy-54nr",
            // Powerball rows carry 6 numbers: five whites then the Powerball.
            WinningNumbersCount: 6,
            SpecialIsLastWinningNumber: true,
            SnapshotResource: "powerball-history.json"),
        new(
            Game.MegaMillions,
            SocrataDataset: "5xaw-6ayf",
            // Mega Millions rows carry 5 whites; the Mega Ball has its own field.
            WinningNumbersCount: 5,
            SpecialIsLastWinningNumber: false,
            SnapshotResource: "megamillions-history.json"),
    ];

    private static readonly FrozenDictionary<Game, GameFeedProfile> ByGame =
        All.ToFrozenDictionary(p => p.Game);

    public static GameFeedProfile For(Game game) =>
        ByGame.TryGetValue(game, out var profile)
            ? profile
            : throw new ArgumentOutOfRangeException(nameof(game), game,
                $"No {nameof(GameFeedProfile)} is registered for {game}. Add one to {nameof(GameFeedProfiles)}.");
}
