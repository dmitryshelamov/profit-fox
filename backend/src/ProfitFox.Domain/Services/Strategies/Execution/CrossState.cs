namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Remembers <c>a</c> and <c>b</c> of the previous closed bar.</summary>
internal sealed class CrossState
{
    public decimal? PreviousA { get; set; }

    public decimal? PreviousB { get; set; }
}