namespace ProfitFox.Bench.Presentation.Instruments;

/// <summary>
///     A pair the preview can be asked for: its id in the market-data catalog and its symbol.
/// </summary>
public sealed record InstrumentResponse(string Id, string Symbol);
