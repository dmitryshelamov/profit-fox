namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>The input of the node on the last closed bar, and the one before it that the node gives.</summary>
internal sealed class PreviousState
{
    private decimal? _last;

    public decimal? Previous { get; private set; }

    public void Close(decimal? value)
    {
        Previous = _last;
        _last = value;
    }
}