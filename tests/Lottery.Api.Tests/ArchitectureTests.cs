using System.Reflection;
using NetArchTest.Rules;

namespace Lottery.Api.Tests;

/// <summary>
/// The onion, held by tests instead of by review. The csproj graph constrains
/// which ASSEMBLIES a layer may reference, but nothing constrains what the
/// code inside them actually touches - one careless using and Domain is
/// talking to Dapper. These rules read the compiled assemblies and pin the
/// documented direction: Domain knows nothing, Application reaches only
/// inward, Infrastructure never sees the Api. Deterministic by construction -
/// the input is the build output, so a run cannot flake.
/// </summary>
public sealed class ArchitectureTests
{
    // One exported anchor per assembly; typeof keeps the lookup refactor-safe
    // where a name string would silently start scanning nothing.
    private static readonly Assembly DomainAssembly = typeof(Lottery.Domain.Draw).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Lottery.Application.UseCases.GetDraws).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Lottery.Infrastructure.DependencyInjection).Assembly;

    // Persistence and web stacks, by namespace prefix. "Microsoft.Data" covers
    // both Sqlite and SqlClient, "System.Data" is ADO.NET (IDbConnection is how
    // Dapper usage leaks upward), "DbUp" is migrations, "Microsoft.AspNetCore"
    // is the whole web stack. Inner layers must compile without any of them.
    private static readonly string[] PersistenceAndWebLibraries =
    [
        "Dapper",
        "DbUp",
        "Microsoft.Data",
        "System.Data",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    public void Domain_DependsOnNoOtherLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Lottery.Application", "Lottery.Infrastructure", "Lottery.Api")
            .GetResult();

        AssertHolds(result, "Lottery.Domain must not depend on any other layer.");
    }

    [Fact]
    public void Domain_UsesNoPersistenceOrWebLibrary()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(PersistenceAndWebLibraries)
            .GetResult();

        AssertHolds(result, "Lottery.Domain must stay free of persistence and web libraries.");
    }

    [Fact]
    public void Application_ReachesOnlyInward()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("Lottery.Infrastructure", "Lottery.Api")
            .GetResult();

        AssertHolds(result, "Lottery.Application may depend on Lottery.Domain only, never outward.");
    }

    [Fact]
    public void Application_IsPersistenceFree_PortsOnly()
    {
        // Verified against reality, not just the docs: Lottery.Application.csproj
        // declares zero PackageReferences. Data access enters as ports
        // (Abstractions/IDrawRepository, IHistorySource, ...) that Infrastructure
        // implements - this rule is what keeps the next repository from being
        // "just one small Dapper query" inside a use case.
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(PersistenceAndWebLibraries)
            .GetResult();

        AssertHolds(result, "Lottery.Application must stay persistence-free - data access goes through its ports.");
    }

    [Fact]
    public void Infrastructure_NeverReferencesTheApi()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny("Lottery.Api")
            .GetResult();

        AssertHolds(result, "Lottery.Infrastructure must never reference Lottery.Api - composition flows the other way.");
    }

    // A bare IsSuccessful assert would report "False is not True" - useless at
    // 2 a.m. in a CI log. Name the broken rule and every offending type.
    private static void AssertHolds(TestResult result, string rule)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = string.Join(
            Environment.NewLine,
            (result.FailingTypes ?? []).Select(type => $"  {type.FullName}"));

        Assert.Fail($"{rule}{Environment.NewLine}Offending types:{Environment.NewLine}{offenders}");
    }
}
