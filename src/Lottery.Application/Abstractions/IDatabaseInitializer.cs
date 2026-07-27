namespace Lottery.Application.Abstractions;

/// <summary>Runs schema migrations; called once at startup before anything touches the database.</summary>
public interface IDatabaseInitializer
{
    void Initialize();
}
