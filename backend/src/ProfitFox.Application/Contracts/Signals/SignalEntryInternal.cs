using ProfitFox.Domain.ValueObjects.Ideas;

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

    /// <summary>
    ///     The type of the output of the latest version, <c>event</c> or <c>boolean</c>; null for a signal that does not read
    ///     (change add-ideas-and-market).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    ///     The label of the signal (change add-ideas-and-market); null when it never had one.
    /// </summary>
    public SignalLabel? Label { get; init; }
}
