using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Commands.Trades.LiftHalt;

/// <summary>
///     «Снять запрет» (change take-idea-trades, design.md, decision 11; spec trade-risk «Снятие запрета»): the ban of the
///     emergency stop is lifted by the owner only, not while a trade still closes; the loss of the day then counts anew from
///     the lift.
/// </summary>
public sealed record LiftHaltCommand : IRequest<TradeHalt>;

internal sealed class LiftHaltCommandHandler(ITradeHaltRepository halt, TimeProvider time) : IRequestHandler<LiftHaltCommand, TradeHalt>
{
    /// <exception cref="TradeConflictException">A trade still closes.</exception>
    public async Task<TradeHalt> Handle(LiftHaltCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        if (!await halt.LiftAsync(now, cancellationToken))
        {
            throw new TradeConflictException("Дождитесь закрытия сделок: снять запрет можно, когда ни одна не закрывается.");
        }

        return await halt.GetAsync(cancellationToken);
    }
}
