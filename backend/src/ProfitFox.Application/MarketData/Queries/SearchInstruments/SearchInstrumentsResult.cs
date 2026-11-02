using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.MarketData.Queries.SearchInstruments;

/// <summary>Either the found instruments or validation errors keyed by parameter name.</summary>
public sealed record SearchInstrumentsResult(IReadOnlyList<ListedInstrument>? Instruments, IReadOnlyDictionary<string, string> Errors);