namespace ProfitFox.Presentation.Contracts.Strategies;

public sealed class CloneNodeBody
{
    /// <summary>Name of the copy; defaults to the source name with a mark.</summary>
    public string? Name { get; init; }
}