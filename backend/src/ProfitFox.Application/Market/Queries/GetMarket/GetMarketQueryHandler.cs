using MediatR;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Application.Paging;
using ProfitFox.Application.SavedFilters;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.SavedFilters;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Market.Queries.GetMarket;

public sealed class GetMarketQueryHandler(
    IMarketSnapshotRepository snapshots,
    IScannerStateRepository state,
    ISavedFilterRepository filters,
    IIdeaReplayRepository cursors,
    ScannerSettings scannerSettings,
    TimeProvider time)
    : IRequestHandler<GetMarketQuery, GetMarketResult>
{
    // The scanner follows Bybit only: moex gets no chip until the ideas of MOEX come (design of add-moex-market, decision 11).
    private static readonly MarketCategory[] ScannerCategories = [MarketCategory.Spot, MarketCategory.Linear];

    private static readonly IdeaSetup[] SetupChips = Enum.GetValues<IdeaSetup>();

    public async Task<GetMarketResult> Handle(GetMarketQuery request, CancellationToken cancellationToken)
    {
        ScannerState scanner = await state.GetAsync(cancellationToken);
        scanner = scanner.RestPausedUntil > time.GetUtcNow() ? scanner : scanner with { RestPausedUntil = null };
        var errors = new Dictionary<string, string>();
        MarketCategory category = default;
        IdeaSetup setup = default;
        Interval interval = Interval.FifteenMinutes;
        MarketSortColumn column = MarketSortColumn.Change1h;
        OrderDirection direction = OrderDirection.Descending;

        if (request.Category is not null && !MarketDataCodes.TryParseCategory(request.Category, out category))
        {
            errors["category"] = MarketQueries.InvalidCategory;
        }

        if (request.Setup is not null && !IdeaCodes.TryParseSetup(request.Setup, out setup))
        {
            errors["setup"] = IdeaQueries.InvalidSetup;
        }

        if (request.Interval is not null && !IdeaQueries.TryParseInterval(request.Interval, out interval))
        {
            errors["interval"] = IdeaQueries.InvalidInterval;
        }

        if (request.Sort is not null && !MarketOrder.TryParseColumn(request.Sort, out column))
        {
            errors["sort"] = MarketOrder.InvalidColumn;
        }

        if (request.Direction is not null && !OrderDirections.TryParse(request.Direction, out direction))
        {
            errors["direction"] = OrderDirections.Invalid;
        }

        var page = PageRequest.Parse(request.Page, request.PageSize, errors);
        SavedFilter? filter = null;

        if (request.SavedFilter is { } filterId)
        {
            filter = await filters.GetAsync(filterId, cancellationToken);

            if (filter is null)
            {
                errors["savedFilter"] = SavedFilterQueries.NotFound;
            }
        }

        if (errors.Count > 0)
        {
            return new GetMarketResult(scanner, null, new Dictionary<Instrument, IReadOnlyList<PairSetup>>(), null, errors);
        }

        // The progress of the replay of the version of the rules this build runs.
        scanner = scanner with { Replay = await cursors.ProgressAsync(IdeasEngineVersion.Current, IdeasReplay.HistoryStart(time.GetUtcNow(), scannerSettings.HistoryDays), cancellationToken) };
        IReadOnlyList<MarketSnapshotRow> rows = await snapshots.GetAllAsync(cancellationToken);
        IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups = await snapshots.GetSetupsAsync(cancellationToken);
        MarketSnapshotRow[] left =
        [
            .. rows.Where(row =>
                (request.Category is null || row.Instrument.Category == category)
                && (request.Search is null || row.Instrument.Symbol.Contains(request.Search, StringComparison.OrdinalIgnoreCase))
                && (filter is null || SavedFilterMatch.Pair(filter.Conditions, row, SetupsOf(setups, row))))
        ];
        MarketSnapshotRow[] chosen = [.. left.Where(row => request.Setup is null || Holds(setups, row, interval, setup))];
        var counts = new MarketCounts(
            rows.Count,
            ScannerCategories.ToDictionary(each => each, each => rows.Count(row => row.Instrument.Category == each)),
            left.Length,
            SetupChips.ToDictionary(chip => chip, chip => left.Count(row => Holds(setups, row, interval, chip))));

        return new GetMarketResult(scanner, page.Slice(MarketOrder.Sort(chosen, column, direction)), setups, counts, errors);
    }

    private static IReadOnlyList<PairSetup> SetupsOf(IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups, MarketSnapshotRow row)
    {
        return setups.GetValueOrDefault(row.Instrument) ?? [];
    }

    private static bool Holds(IReadOnlyDictionary<Instrument, IReadOnlyList<PairSetup>> setups, MarketSnapshotRow row, Interval interval, IdeaSetup setup)
    {
        return SetupsOf(setups, row).Contains(new PairSetup(interval, setup));
    }
}