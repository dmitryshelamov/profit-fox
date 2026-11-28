using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The settings of the risk module (change take-idea-trades, design.md, decision 12): one row; before the first save, the
///     empty settings with the default times and attempts.
/// </summary>
public interface ITradeSettingsRepository
{
    Task<TradeSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps the settings in place of those before.
    /// </summary>
    Task SaveAsync(TradeSettings settings, CancellationToken cancellationToken);
}
