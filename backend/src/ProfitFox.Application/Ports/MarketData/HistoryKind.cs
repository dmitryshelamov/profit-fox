namespace ProfitFox.Application.Ports.MarketData;

/// <summary>
///     The kind of history a month of the cache holds (change add-idea-score, design.md, decision 6). The numbers are those the
///     files of the cache write: candles and funding keep the numbers they had before the kind was named.
/// </summary>
public enum HistoryKind : byte
{
    Candles = 1,
    Funding = 2,
    OpenInterest = 3
}
