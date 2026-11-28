using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Services.Trades;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Commands.Trades.CloseTrade;

/// <summary>
///     «Закрыть по рынку» (change take-idea-trades, design.md, decision 10; spec idea-trades «Закрытие вручную»): a close
///     order «вручную» is written and the trade closes; the trading Worker sends it. Only an open trade closes so.
/// </summary>
public sealed record CloseTradeCommand(Guid Id) : IRequest<Trade>;

internal sealed class CloseTradeCommandHandler(ITradeRepository trades, TimeProvider time) : IRequestHandler<CloseTradeCommand, Trade>
{
    /// <exception cref="KeyNotFoundException">There is no trade of the id.</exception>
    /// <exception cref="TradeConflictException">The trade is not open.</exception>
    public async Task<Trade> Handle(CloseTradeCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        TradeState? state = null;
        TradeBook book = await trades.ChangeAsync(request.Id, null, changed =>
        {
            state = changed.Trade.State;
            return TradeEventHandler.AddClose(changed, TradeCloseReason.Manual, now);
        }, null, cancellationToken) ?? throw new KeyNotFoundException($"There is no trade {request.Id}.");
        return state switch
        {
            TradeState.Open => book.Trade,
            TradeState.Pending => throw new TradeConflictException("Сделка ещё ждёт входа: закрыть её нечем."),
            TradeState.Closing => throw new TradeConflictException("Сделка уже закрывается."),
            TradeState.Error => throw new TradeConflictException("Сделка в «Ошибке»: закройте позицию на Bybit и нажмите «Разобрана»."),
            _ => throw new TradeConflictException("Сделка уже закрыта.")
        };
    }
}
