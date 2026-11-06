using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.Paging;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.IdeaTrades.Queries.GetIdeaJournal;

public sealed class GetIdeaJournalQueryHandler(
    IIdeaTradeRepository trades,
    IPaperIdeaTradeRepository paper,
    IIdeaAutoRepository autos,
    IExchangeAccountRepository accounts,
    IMarketHistoryClient history,
    BacktestSettings backtests,
    TimeProvider time) : IRequestHandler<GetIdeaJournalQuery, GetIdeaJournalResult>
{
    public const string InvalidBadge = "Badge must be up, flat, down or none.";
    public const string InvalidDays = "Days must be 7, 30 or 90.";
    public const string InvalidOrigin = "Origin must be manual, auto or paper.";
    public const string InvalidAuto = "There is no such auto.";

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

        if (request.Origin is not null and not ("manual" or "auto" or "paper"))
        {
            errors["origin"] = InvalidOrigin;
        }

        IReadOnlyDictionary<Guid, IdeaAuto> everyAuto = await autos.GetEveryAsync(cancellationToken);

        if (request.AutoId is { } autoId && !everyAuto.ContainsKey(autoId))
        {
            errors["autoId"] = InvalidAuto;
        }

        var page = PageRequest.Parse(request.Page, request.PageSize, errors);

        if (errors.Count > 0)
        {
            return new GetIdeaJournalResult(null, errors);
        }

        var names = (await accounts.GetAllAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        IReadOnlyList<(IdeaTrade Trade, bool Paper)> matching =
        [
            .. (await trades.GetAllAsync(cancellationToken)).Select(trade => (Trade: trade, Paper: false)).Concat((await paper.GetAllAsync(cancellationToken)).Select(item => (Trade: item.Trade, Paper: true)))
                .Where(item =>
                    (request.Setup is null || item.Trade.Setup == setup)
                    && (request.Side is null || item.Trade.Side == side)
                    && (request.Symbol is null || item.Trade.Instrument.Symbol.Contains(request.Symbol, StringComparison.OrdinalIgnoreCase))
                    && (request.Badge is null || (request.Badge == "none" ? item.Trade.Promise?.Badge is null : item.Trade.Promise?.Badge == badge))
                    && (request.Origin is null || OriginOf(item) == request.Origin)
                    && (request.AutoId is null || item.Trade.AutoId == request.AutoId))
        ];
        DateTimeOffset? since = request.Days is { } period ? time.GetUtcNow() - TimeSpan.FromDays(period) : null;
        IReadOnlyList<(IdeaTrade Trade, bool Paper)> closed =
        [
            .. matching.Where(item => !item.Trade.IsOpen && (since is null || item.Trade.StatusAt >= since)).OrderByDescending(item => item.Trade.StatusAt).ThenBy(item => item.Trade.Id)
        ];
        IReadOnlyList<(IdeaTrade Trade, bool Paper)> opened = [.. matching.Where(item => item.Trade.IsOpen).OrderByDescending(item => item.Trade.CreatedAt).ThenBy(item => item.Trade.Id)];
        IReadOnlyDictionary<Instrument, decimal> prices = await PricesAsync(opened.Select(item => item.Trade), cancellationToken);
        IReadOnlyList<IdeaJournalTrade> open = [.. opened.Select(item => Worth(item.Trade, AccountName(names, item.Trade.AccountId), prices, item.Paper, everyAuto))];

        return new GetIdeaJournalResult(
            new IdeaJournal(
                open,
                page.Slice([.. closed.Select(item => Of(item.Trade, AccountName(names, item.Trade.AccountId), item.Paper, everyAuto))]),
                IdeaJournalTotals.Compute([.. closed.Where(item => !item.Paper).Select(item => item.Trade)]),
                IdeaJournalTotals.Compute([.. closed.Where(item => item.Paper).Select(item => item.Trade)])),
            errors);
    }

    // A paper trade has no foreign key to its account: the account of a deleted auto may be gone, and the trade stays with no name of it.
    private static string AccountName(IReadOnlyDictionary<Guid, string> names, Guid account)
    {
        return names.GetValueOrDefault(account, "—");
    }

    private static string OriginOf((IdeaTrade Trade, bool Paper) item)
    {
        return item.Paper ? "paper" : item.Trade.AutoId is null ? "manual" : "auto";
    }

    private static IdeaJournalTrade Of(IdeaTrade trade, string accountName, bool paper, IReadOnlyDictionary<Guid, IdeaAuto> everyAuto)
    {
        return new IdeaJournalTrade(trade, accountName, Origin: paper ? IdeaTradeOrigin.Paper : trade.AutoId is null ? IdeaTradeOrigin.Manual : IdeaTradeOrigin.Auto, AutoName: trade.AutoId is { } id ? everyAuto[id].Name : null);
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

    private IdeaJournalTrade Worth(IdeaTrade trade, string accountName, IReadOnlyDictionary<Instrument, decimal> prices, bool paper, IReadOnlyDictionary<Guid, IdeaAuto> everyAuto)
    {
        IdeaJournalTrade view = Of(trade, accountName, paper, everyAuto);

        if (!prices.TryGetValue(trade.Instrument, out decimal price))
        {
            return view;
        }

        decimal taker = backtests.Policy().DefaultFees[trade.Instrument.Category].MarketPercent;

        return view with { LastPrice = price, CurrentResult = trade.ResultAt(price, taker), CurrentResultR = trade.ResultRAt(price, taker) };
    }
}