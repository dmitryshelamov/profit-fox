namespace ProfitFox.Infrastructure.Persistence.Files.HistoryCache;

/// <summary>
///     The cache of history of the process (change add-backtest-history-cache, design.md, decision 10): its folder, a full
///     path, and how many bytes its files may take.
/// </summary>
internal sealed record HistoryCacheSettings(string Path, long MaxBytes);
