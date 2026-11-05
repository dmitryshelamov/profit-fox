namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>What an indicator node remembers of the closed bars it was given.</summary>
internal interface IIndicatorState
{
    void Add(decimal value, decimal period);
}