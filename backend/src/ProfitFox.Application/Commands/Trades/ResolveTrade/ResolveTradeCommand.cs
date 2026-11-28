using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Commands.Trades.ResolveTrade;

/// <summary>
///     «Разобрана» (change take-idea-trades, design.md, decisions 10 and 16; spec idea-trades «Ошибку разбирает владелец»): the
///     position of the pair is read at Bybit; when there is none, the trade in «Ошибка» is closed «на бирже» with the outcome of
///     the fills the bench found, and the pair may be entered again. When Bybit no longer takes the key of the account or its
///     secret does not read, the position cannot be read: with <see cref="Unchecked" /> — the owner confirmed the position on
///     Bybit is closed — the trade is closed so, «без проверки»; the flag is taken only when the check is impossible so.
/// </summary>
public sealed record ResolveTradeCommand(Guid Id, bool Unchecked = false) : IRequest<Trade>;

internal sealed class ResolveTradeCommandHandler(
    ITradeRepository trades,
    IExchangeAccountRepository accounts,
    IExchangeTrading trading,
    ISecretProtector protector,
    TimeProvider time) : IRequestHandler<ResolveTradeCommand, Trade>
{
    /// <exception cref="KeyNotFoundException">There is no trade of the id.</exception>
    /// <exception cref="TradeConflictException">The trade is not in error, or its position is still at Bybit.</exception>
    /// <exception cref="ExchangeUnavailableException">Bybit did not answer.</exception>
    /// <exception cref="TradeUncheckedException">The position cannot be read, and the owner did not confirm it closed.</exception>
    public async Task<Trade> Handle(ResolveTradeCommand request, CancellationToken cancellationToken)
    {
        Trade trade = await trades.FindAsync(request.Id, cancellationToken) ?? throw new KeyNotFoundException($"There is no trade {request.Id}.");
        if (trade.State != TradeState.Error)
        {
            throw new TradeConflictException("Разобрать можно только сделку в «Ошибке».");
        }

        string? skipped = null;
        if (trade.AccountId is { } accountId && await accounts.FindAsync(accountId, cancellationToken) is { } account
                                             && await accounts.SecretAsync(accountId, cancellationToken) is { } secret)
        {
            try
            {
                var credentials = new ExchangeCredentials(account.ApiKey, protector.Reveal(account.Id, secret));
                IReadOnlyList<ExchangePosition> positions = await trading.PositionsAsync(credentials, trade.Symbol, cancellationToken);
                if (positions.Any(position => position.Size > 0))
                {
                    throw new TradeConflictException("Сначала закройте позицию на Bybit.");
                }
            }
            catch (ExchangeKeyRejectedException exception)
            {
                skipped = request.Unchecked ? "Bybit не принимает ключ аккаунта" : throw new TradeUncheckedException($"Позицию не проверить: Bybit не принимает ключ аккаунта ({exception.Message}). Если позиция на Bybit закрыта, подтвердите это.", exception);
            }
            catch (SecretUnreadableException exception)
            {
                skipped = request.Unchecked ? "секрет аккаунта не читается" : throw new TradeUncheckedException("Позицию не проверить: секрет аккаунта не читается. Если позиция на Bybit закрыта, подтвердите это.", exception);
            }
        }

        DateTimeOffset now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        TradeBook book = (await trades.ChangeAsync(trade.Id, null, changed =>
        {
            if (changed.Trade.State != TradeState.Error)
            {
                return false;
            }

            changed.Trade.Close(TradeCloseReason.Exchange, now, changed.Trade.Numbers.Result);
            if (skipped is not null)
            {
                changed.Trade.Reason = $"разобрана без проверки на бирже: {skipped}";
            }

            return true;
        }, null, cancellationToken))!;
        return book.Trade.State == TradeState.Closed ? book.Trade : throw new TradeConflictException("Разобрать можно только сделку в «Ошибке».");
    }
}
