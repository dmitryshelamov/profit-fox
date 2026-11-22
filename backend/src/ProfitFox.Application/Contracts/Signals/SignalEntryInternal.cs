namespace ProfitFox.Application.Contracts.Signals;

/// <summary>
///     A signal in the list of the library: its name, description and latest version, or why it does not read. The id is
///     known for a signal that reads, and so are the strategies any version of which points at it, in name order (spec
///     signal-constructor «Стратегии сигнала в списке»).
/// </summary>
public sealed record SignalEntryInternal(string Name, string? Description, int Version, string? Error)
{
    public Guid? Id { get; init; }

    public IReadOnlyList<string> Strategies { get; init; } = [];
}
