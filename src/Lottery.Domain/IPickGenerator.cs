namespace Lottery.Domain;

/// <summary>Domain service port for generating random ticket picks.</summary>
public interface IPickGenerator
{
    /// <summary>Five distinct sorted whites + one special, valid for the given era.</summary>
    (IReadOnlyList<int> WhiteBalls, int Special) Generate(RuleEra era);
}

/// <summary>
/// Uniform picks via partial Fisher-Yates shuffle - no retry loop, provably uniform.
/// Production uses Random.Shared; tests inject a seeded Random for determinism.
/// Non-cryptographic randomness is deliberate (decision D21): these are play
/// suggestions, not stake-settling draws - the real winning numbers come from
/// the lotteries' physical machines, so nothing here depends on
/// unpredictability. If an RNG ever decides a real outcome in this system,
/// switch to a CSPRNG (RandomNumberGenerator.GetInt32) behind this same
/// injection seam.
/// </summary>
public sealed class RandomPickGenerator(Random? random = null) : IPickGenerator
{
    private readonly Random _random = random ?? Random.Shared;

    public (IReadOnlyList<int> WhiteBalls, int Special) Generate(RuleEra era)
    {
        var pool = Enumerable.Range(1, era.WhiteBallMax).ToArray();
        for (var i = 0; i < RuleEra.WhiteBallCount; i++)
        {
            var j = _random.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var whites = pool.Take(RuleEra.WhiteBallCount).OrderBy(n => n).ToArray();
        var special = _random.Next(1, era.SpecialBallMax + 1);
        return (whites, special);
    }
}
