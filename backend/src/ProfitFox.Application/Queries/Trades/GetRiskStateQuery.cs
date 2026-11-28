using MediatR;

using ProfitFox.Application.Contracts.Trades;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Trades;

/// <summary>
///     The state of the risk module from the base (change take-idea-trades, design.md, decisions 11 and 13): the loss of the
///     day by the outcomes closed since its start and the results of the open trades the trading Worker wrote, the counters
///     against the limits, the emergency stop, the trading Worker and how the accounts are read. Bybit is not called.
/// </summary>
public sealed record GetRiskStateQuery : IRequest<RiskStateInternal>;

internal sealed class GetRiskStateQueryHandler(
    ITradeSettingsRepository settings,
    ITradeRepository trades,
    ITradeHaltRepository halt,
    ITraderLeaseRepository trader,
    IExchangeAccountRepository accounts,
    TimeProvider time) : IRequestHandler<GetRiskStateQuery, RiskStateInternal>
{
    public async Task<RiskStateInternal> Handle(GetRiskStateQuery request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        TradeSettings risk = await settings.GetAsync(cancellationToken);
        TradeHalt ban = await halt.GetAsync(cancellationToken);
        (IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> closed, IReadOnlyList<decimal> open) = await trades.DayAsync(DayLoss.Start(now, ban.LiftedAt), cancellationToken);
        (int count, decimal amount, _) = await trades.CountersAsync(null, "", cancellationToken);
        IReadOnlyList<TradeBook> holding = await trades.HoldingAsync(cancellationToken);
        TraderState? lease = await trader.StateAsync(cancellationToken);
        IReadOnlyList<ExchangeAccount> all = await accounts.AllAsync(cancellationToken);
        return new RiskStateInternal(
            risk,
            TradeSettingsRules.Missing(risk),
            DayLoss.Count(closed, open, now, ban.LiftedAt),
            count,
            amount,
            ban,
            ban.Halted ? [.. holding.Where(book => book.Trade.State == TradeState.Error).Select(book => book.Trade.Symbol)] : [],
            lease?.Alive ?? false,
            lease?.Alive == true ? lease.Worker : null,
            all.Count,
            all.Count(account => account.Feed == "stream"),
            [.. all.Where(account => account.Feed == "poll").Select(account => new PolledAccountInternal(account.Id, account.Name, account.FeedChangedAt ?? now))]);
    }
}
