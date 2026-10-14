using Lottery.Domain;

namespace Lottery.Domain.Tests;

/// <summary>
/// The registry that replaced ten scattered conditionals on <see cref="Game"/>.
/// </summary>
/// <remarks>
/// Seven of those were two-way ternaries - <c>game == Game.Powerball ? x : y</c> - which are
/// correct for two games and silently wrong for three. The tests that matter here are therefore
/// not "Powerball returns Powerball's values", which the old code did fine too, but the ones a
/// ternary could never have passed: an unregistered game must be refused rather than quietly
/// treated as Mega Millions.
/// </remarks>
public class GameProfilesTests
{
    /// <summary>A value outside the enum, standing in for a third game added later.</summary>
    private const Game Unregistered = (Game)99;

    [Fact]
    public void EveryDeclaredGame_HasAProfile()
    {
        foreach (var game in Enum.GetValues<Game>())
        {
            var profile = GameProfiles.For(game);

            Assert.Equal(game, profile.Game);
            Assert.NotEmpty(profile.Slug);
            Assert.NotEmpty(profile.UrlAliases);
            Assert.NotEmpty(profile.SpecialBallName);
            Assert.NotEmpty(profile.DrawDays);
            Assert.NotEmpty(profile.PrizeTiers);
        }
    }

    [Fact]
    public void AnUnregisteredGame_IsRefused_NotSilentlyTreatedAsAnother()
    {
        // The whole point of the registry. Before, PrizeTiers.For((Game)99) returned Mega
        // Millions' table, the Socrata feed queried Mega Millions' dataset, and the snapshot
        // source loaded Mega Millions' history - all without error.
        Assert.Throws<ArgumentOutOfRangeException>(() => GameProfiles.For(Unregistered));
    }

    [Fact]
    public void PrizeTiers_AlsoRefuseAnUnregisteredGame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrizeTiers.For(Unregistered));
    }

    [Theory]
    [InlineData("powerball", Game.Powerball)]
    [InlineData("POWERBALL", Game.Powerball)]
    [InlineData("power-ball", Game.Powerball)]
    [InlineData("megamillions", Game.MegaMillions)]
    [InlineData("Mega-Millions", Game.MegaMillions)]
    public void KnownAliases_Parse(string raw, Game expected)
    {
        Assert.True(GameProfiles.TryParse(raw, out var game));
        Assert.Equal(expected, game);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lotto")]
    [InlineData(null)]
    public void UnknownAliases_DoNotParse(string? raw)
    {
        Assert.False(GameProfiles.TryParse(raw, out _));
    }

    [Fact]
    public void EveryProfilesCanonicalSlug_IsOneOfItsOwnAliases()
    {
        // Otherwise a game is advertised at a URL that does not route to it - the API's
        // "unknown game" message is built from these slugs.
        foreach (var profile in GameProfiles.All)
        {
            Assert.Contains(profile.Slug, profile.UrlAliases);
            Assert.True(GameProfiles.TryParse(profile.Slug, out var parsed));
            Assert.Equal(profile.Game, parsed);
        }
    }

    [Fact]
    public void KnownSlugs_ListsEveryGame()
    {
        var message = GameProfiles.KnownSlugs;

        foreach (var profile in GameProfiles.All)
            Assert.Contains(profile.Slug, message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExtensionMethods_AgreeWithTheProfile()
    {
        foreach (var game in Enum.GetValues<Game>())
        {
            var profile = GameProfiles.For(game);

            Assert.Equal(profile.DrawDays, game.DrawDays());
            Assert.Equal(profile.DrawTimeEastern, game.DrawTimeEastern());
            Assert.Equal(profile.SpecialBallName, game.SpecialBallName());
            Assert.Equal(profile.Slug, game.Slug());
        }
    }
}
