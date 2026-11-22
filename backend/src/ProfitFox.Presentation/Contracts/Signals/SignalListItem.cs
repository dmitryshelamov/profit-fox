using ProfitFox.Presentation.Contracts.Ideas;

namespace ProfitFox.Presentation.Contracts.Signals;

/// <summary>
///     A signal in the list (spec signal-constructor «Список сигналов»): its name, description and latest version, or why it
///     does not read, its id (spec signal-constructor «Идентификатор сигнала»), known for a signal that reads, and the names
///     of the strategies any version of which points at it (spec signal-constructor «Стратегии сигнала в списке»).
///     <see cref="Output" /> is the type of the output of the latest version, <c>event</c> or <c>boolean</c> (change
///     add-ideas-and-market), and the label the signal puts on ideas, null when it never had one.
/// </summary>
public sealed record SignalListItem(string Name, string? Description, int Version, string? Error, Guid? Id, IReadOnlyList<string> Strategies, string? Output, SignalLabelResponse? Label = null);
