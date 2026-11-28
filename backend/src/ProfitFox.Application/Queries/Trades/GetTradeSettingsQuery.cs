using MediatR;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Queries.Trades;

/// <summary>
///     The settings of the risk module; before the first save, no limit and the default times and attempts (spec trade-risk
///     «Значения по умолчанию»).
/// </summary>
public sealed record GetTradeSettingsQuery : IRequest<TradeSettings>;

internal sealed class GetTradeSettingsQueryHandler(ITradeSettingsRepository settings) : IRequestHandler<GetTradeSettingsQuery, TradeSettings>
{
    public Task<TradeSettings> Handle(GetTradeSettingsQuery request, CancellationToken cancellationToken)
    {
        return settings.GetAsync(cancellationToken);
    }
}
