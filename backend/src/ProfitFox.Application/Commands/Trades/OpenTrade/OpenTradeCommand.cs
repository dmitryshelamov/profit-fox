using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Commands.Trades.OpenTrade;

/// <summary>
///     The entry by an idea (change take-idea-trades, design.md, decisions 2, 4 and 7; spec idea-trades «Вход в идею по
///     клику»): every check again on data read anew, and the trade written with its entry order in one transaction, the limits
///     counted anew under the lock of the entry. The order goes to Bybit from the trading Worker, not from here: the API only
///     reads Bybit. The trade gets the leverage, the wait for the entry and the attempts to put the stop back of the settings
///     now, and keeps them.
/// </summary>
public sealed record OpenTradeCommand(Guid IdeaId, Guid? AccountId, decimal? Amount) : IRequest<Trade>;

internal sealed class OpenTradeCommandHandler(EntryContextReader reader, ITradeRepository trades, TimeProvider time) : IRequestHandler<OpenTradeCommand, Trade>
{
    /// <exception cref="TradeEntryRefusedException">A check did not pass; nothing was written.</exception>
    /// <exception cref="KeyNotFoundException">There is no idea or no account of the id.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="ExchangeException">Bybit refused a read for another reason.</exception>
    public async Task<Trade> Handle(OpenTradeCommand request, CancellationToken cancellationToken)
    {
        EntryContext context = await reader.ReadAsync(request.IdeaId, request.AccountId, request.Amount, cancellationToken);
        EntryCalculation count = EntryCalculator.Count(context.Input);
        if (!count.Passed || context.Account is null || count.Stop is null || count.StopSource is null)
        {
            throw new TradeEntryRefusedException([.. count.Checks.Where(check => !check.Passed)]);
        }

        TradeSettings settings = context.Input.Settings;
        DateTimeOffset now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        var trade = new Trade
        {
            Id = Guid.NewGuid(),
            IdeaId = context.Input.Idea.Id,
            Symbol = context.Input.Idea.Symbol,
            Side = count.Side,
            StrategyId = context.Input.Idea.StrategyId,
            Strategy = context.Input.Idea.StrategyName,
            Version = context.Input.Idea.StrategyVersion,
            Timeframe = context.Input.Idea.Timeframe,
            IdeaMoment = context.Input.Idea.Moment,
            Until = context.Input.Idea.Until,
            IdeaEntry = count.IdeaEntry,
            AccountId = context.Account.Id,
            AccountName = context.Account.Name,
            Amount = count.Amount,
            Leverage = count.Leverage,
            ClickPrice = count.Price,
            Quantity = count.Quantity,
            Stop = count.Stop.Value,
            StopSource = count.StopSource.Value,
            Target = count.Target,
            EntryWait = settings.EntryWait,
            StopAttempts = settings.StopAttempts,
            CreatedAt = now
        };
        var entry = new TradeOrder(trade.Id, 1, TradeOrderPurpose.Entry, null, trade.Quantity, TradeOrderStatus.Pending, 0, now, null, null, now);
        if (await trades.OpenAsync(trade, entry, settings.MaxOpenTrades!.Value, settings.MaxTotalAmount!.Value, cancellationToken) is { } refusal)
        {
            throw new TradeEntryRefusedException([refusal.Check]);
        }

        return trade;
    }
}
