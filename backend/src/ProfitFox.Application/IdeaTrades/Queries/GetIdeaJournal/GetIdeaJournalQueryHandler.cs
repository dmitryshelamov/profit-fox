using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.Paging;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;

public sealed class GetIdeaJournalQueryHandler(
    IIdeaTradeRepository trades,
    IExchangeAccountRepository accounts,
    IMarketHistoryClient history,
    BacktestSettings backtests,
    TimeProvider time) : IRequestHandler<GetIdeaJournalQuery, GetIdeaJournalResult>
{
    public const string InvalidBadge = "Badge must be up, flat, down or none.";
    public const string InvalidDays = "Days must be 7, 30 or 90.";

    private static readonly int[] Periods = [7, 30, 90];

    public async Task<GetIdeaJournalResult> Handle(GetIdeaJournalQuery request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        IdeaSetup setup = default;
        IdeaSide side = default;
        IdeaBadge badge = default;

        if (request.Setup is not null && !IdeaCodes.TryParseSetup(request.Setup, out setup))
        {
            errors["setup"] = IdeaQueries.InvalidSetup;
        }

        if (request.Side is not null && !IdeaCodes.TryParseSide(request.Side, out side))
        {
            errors["side"] = IdeaQueries.InvalidSide;
        }

        if (request.Badge is not null and not "none" && !IdeaCodes.TryParseBadge(request.Badge, out badge))
        {
            errors["badge"] = InvalidBadge;
        }

        if (request.Days is { } days && !Periods.Contains(days))
        {
            errors["days"] = InvalidDays;
        }

        var page = PageRequest.Parse(request.Page, request.PageSize, errors);

        if (errors.Count > 0)
        {
            return new GetIdeaJournalResult(null, errors);
        }

        var names = (await accounts.GetAllAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        IReadOnlyList<IdeaTrade> matching =
        [
            .. (await trades.GetAllAsync(cancellationToken)).Where(trade =>
                (request.Setup is null || trade.Setup == setup)
                && (request.Side is null || trade.Side == side)
                && (request.Symbol is null || trade.Instrument.Symbol.Contains(request.Symbol, StringComparison.OrdinalIgnoreCase))
                && (request.Badge is null || (request.Badge == "none" ? trade.Promise?.Badge is null : trade.Promise?.Badge == badge)))
        ];
        DateTimeOffset? since = request.Days is { } period ? time.GetUtcNow() - TimeSpan.FromDays(period) : null;
        IReadOnlyList<IdeaTrade> closed =
        [
            .. matching.Where(trade => !trade.IsOpen && (since is null || trade.StatusAt >= since)).OrderByDescending(trade => trade.StatusAt).ThenBy(trade => trade.Id)
        ];
        IReadOnlyList<IdeaTrade> opened = [.. matching.Where(trade => trade.IsOpen).OrderByDescending(trade => trade.CreatedAt).ThenBy(trade => trade.Id)];
        IReadOnlyDictionary<Instrument, decimal> prices = await PricesAsync(opened, cancellationToken);
        IReadOnlyList<IdeaJournalTrade> open = [.. opened.Select(trade => Worth(trade, names[trade.AccountId], prices))];

        return new GetIdeaJournalResult(
            new IdeaJournal(open, page.Slice([.. closed.Select(trade => new IdeaJournalTrade(trade, names[trade.AccountId]))]), IdeaJournalTotals.Compute([.. closed])),
            errors);
    }

    // One request for each symbol of a trade that holds something; an exchange that does not answer leaves the symbol without a price.
    private async Task<IReadOnlyDictionary<Instrument, decimal>> PricesAsync(IEnumerable<IdeaTrade> opened, CancellationToken cancellationToken)
    {
        var prices = new Dictionary<Instrument, decimal>();

        foreach (Instrument instrument in opened.Where(trade => trade.Status == IdeaTradeStatus.Open).Select(trade => trade.Instrument).Distinct())
        {
            try
            {
                prices[instrument] = await history.GetLastPriceAsync(instrument, cancellationToken);
            }
            catch (ExchangeException)
            {
            }
        }

        return prices;
    }

    private IdeaJournalTrade Worth(IdeaTrade trade, string accountName, IReadOnlyDictionary<Instrument, decimal> prices)
    {
        if (!prices.TryGetValue(trade.Instrument, out decimal price))
        {
            return new IdeaJournalTrade(trade, accountName);
        }

        decimal taker = backtests.Policy().DefaultFees[trade.Instrument.Category].MarketPercent;

        return new IdeaJournalTrade(trade, accountName, price, trade.ResultAt(price, taker), trade.ResultRAt(price, taker));
    }
}