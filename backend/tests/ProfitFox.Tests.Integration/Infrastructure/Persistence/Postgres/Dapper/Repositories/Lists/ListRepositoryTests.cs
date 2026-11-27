using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Lists.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;

/// <summary>
///     Change add-ideas-and-market, tasks 10.1–10.3: the lists of the market and of ideas sorted, chosen and paged by the
///     database, the saved filters and their conditions in both places, and the count of matching rows.
/// </summary>
[NonParallelizable]
public sealed class ListRepositoryTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 14, 32, 0, TimeSpan.Zero);
    private IMarketPairRepository _market = null!;
    private IIdeaRepository _ideas = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _market = BenchServices.Get<IMarketPairRepository>();
        _ideas = BenchServices.Get<IIdeaRepository>();
    }

    [Test]
    public async Task A_pair_without_a_value_of_the_column_goes_last_in_either_direction()
    {
        await _market.ReplaceAsync([Pair("AUSDT", change24h: 0.01m), Pair("BUSDT", change24h: null), Pair("CUSDT", change24h: -0.02m)], CancellationToken.None);

        ListPage<MarketPairRow> descending = await _market.ListAsync(new MarketListQuery(new ListOrder("change24h", true, 1, 50)), CancellationToken.None);
        ListPage<MarketPairRow> ascending = await _market.ListAsync(new MarketListQuery(new ListOrder("change24h", false, 1, 50)), CancellationToken.None);

        Assert.That(descending.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "AUSDT", "CUSDT", "BUSDT" }));
        Assert.That(ascending.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "CUSDT", "AUSDT", "BUSDT" }));
    }

    [Test]
    public async Task The_first_page_starts_with_the_largest_turnover_and_the_total_counts_every_pair()
    {
        await _market.ReplaceAsync([.. Enumerable.Range(1, 120).Select(index => Pair($"P{index:D3}USDT", turnover: index * 1_000_000m))], CancellationToken.None);

        ListPage<MarketPairRow> second = await _market.ListAsync(new MarketListQuery(new ListOrder("turnover24h", true, 2, 50)), CancellationToken.None);

        Assert.That((second.Total, second.Page, second.PageSize, second.Items.Count), Is.EqualTo((120, 2, 50, 50)));
        Assert.That(second.Items[0].Pair.Symbol, Is.EqualTo("P070USDT"));
    }

    [Test]
    public async Task A_part_of_the_symbol_is_a_value_not_a_part_of_the_statement()
    {
        await _market.ReplaceAsync([Pair("SOLUSDT"), Pair("BTCUSDC")], CancellationToken.None);

        ListPage<MarketPairRow> attack = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), Search: "'; DROP TABLE market_pairs; --"), CancellationToken.None);
        ListPage<MarketPairRow> part = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), Search: "usdt"), CancellationToken.None);
        ListPage<MarketPairRow> pattern = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), Search: "%"), CancellationToken.None);

        Assert.That(attack.Total, Is.Zero);
        Assert.That(part.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "SOLUSDT" }), "whatever the case");
        Assert.That(pattern.Total, Is.Zero, "a percent sign is a character, not a pattern");
        Assert.That(await Scalar<int>("SELECT count(*) FROM market_pairs"), Is.EqualTo(2));
    }

    /// <summary>
    ///     Spec saved-filters «Сохранённый фильтр»: «Сохранение», «Занятое имя», «Удаление».
    /// </summary>
    [Test]
    public async Task A_filter_is_saved_its_name_is_one_of_its_kind_and_it_is_deleted()
    {
        ISavedFilterRepository filters = BenchServices.Get<ISavedFilterRepository>();
        var high = new SavedFilter(Guid.NewGuid(), "Высокий фандинг", FilterPlace.Market, [new FilterCondition("fundingRate", "ge", "0.03"), new FilterCondition("turnover24h", "gt", "100")]);

        Assert.That(await filters.SaveAsync(high, CancellationToken.None), Is.True);
        Assert.That(await filters.SaveAsync(high with { Id = Guid.NewGuid(), Place = FilterPlace.Ideas }, CancellationToken.None), Is.False, "the name is taken");
        SavedFilter read = (await filters.AllAsync(CancellationToken.None)).Single();
        Assert.That((read.Name, read.Place, read.Conditions), Is.EqualTo((high.Name, high.Place, high.Conditions)).Using<(string, FilterPlace, IReadOnlyList<FilterCondition>)>(
            (a, b) => a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3.SequenceEqual(b.Item3)));

        Assert.That(await filters.DeleteAsync(high.Id, CancellationToken.None), Is.True);
        Assert.That(await filters.FindAsync(high.Id, CancellationToken.None), Is.Null);
    }

    /// <summary>
    ///     Spec saved-filters «Применение фильтра»: «Фильтр и выбор» and «Условие фильтра»: «Между».
    /// </summary>
    [Test]
    public async Task The_pairs_match_both_the_filter_and_the_search()
    {
        await _market.ReplaceAsync(
        [
            Pair("SOLUSDT", funding: 0.0004m, turnover: 200_000_000m),
            Pair("BTCUSDC", funding: 0.0004m, turnover: 200_000_000m),
            Pair("XRPUSDT", funding: 0.0001m, turnover: 200_000_000m),
            Pair("WIFUSDT", funding: 0.0005m, turnover: 50_000_000m)
        ], CancellationToken.None);
        FilterCondition[] high = [new FilterCondition("fundingRate", "ge", "0.03"), new FilterCondition("turnover24h", "gt", "100")];

        ListPage<MarketPairRow> page = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), Search: "USD", Conditions: high), CancellationToken.None);
        ListPage<MarketPairRow> between = await _market.ListAsync(
            new MarketListQuery(new ListOrder("symbol", false, 1, 50), Conditions: [new FilterCondition("fundingRate", "between", "0.01", "0.04")]),
            CancellationToken.None);

        Assert.That(page.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "BTCUSDC", "SOLUSDT" }));
        Assert.That(page.Total, Is.EqualTo(2));
        Assert.That(between.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "BTCUSDC", "SOLUSDT", "XRPUSDT" }), "0.01 % and 0.04 % are inside");
    }

    /// <summary>
    ///     Spec saved-filters «Поле рынка в идеях» and «Поле идеи на рынке»; spec market-overview «Только с идеями» and
    ///     «Стратегия и таймфрейм идеи».
    /// </summary>
    [Test]
    public async Task A_field_of_the_market_chooses_ideas_by_their_pair_and_a_field_of_an_idea_chooses_pairs_by_their_open_ideas()
    {
        await _market.ReplaceAsync([Pair("SOLUSDT", funding: 0.0004m), Pair("BTCUSDT", funding: 0.0001m), Pair("ETHUSDT", funding: 0.0001m)], CancellationToken.None);
        var fundingShort = Guid.NewGuid();
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "SOLUSDT", side: EntrySide.Short, timeframe: TimeSpan.FromHours(4)));
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "BTCUSDT", side: EntrySide.Short, timeframe: TimeSpan.FromHours(1)));
        await Add(IdeaGenerator.Generate(Guid.NewGuid(), name: "breakout-long", symbol: "BTCUSDT", side: EntrySide.Long, timeframe: TimeSpan.FromHours(4)));

        ListPage<Idea> ofHighFunding = await _ideas.ListAsync(
            new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50), Conditions: [new FilterCondition("fundingRate", "ge", "0.03")]),
            CancellationToken.None);
        ListPage<MarketPairRow> withShort4h = await _market.ListAsync(
            new MarketListQuery(new ListOrder("symbol", false, 1, 50), Conditions: [new FilterCondition("side", "eq", "short"), new FilterCondition("timeframe", "eq", "4h")]),
            CancellationToken.None);
        ListPage<MarketPairRow> withIdeas = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), IdeasOnly: true), CancellationToken.None);
        ListPage<MarketPairRow> fundingShort4h = await _market.ListAsync(new MarketListQuery(new ListOrder("symbol", false, 1, 50), Strategy: "funding-short", Timeframe: "4h"), CancellationToken.None);

        Assert.That(ofHighFunding.Items.Select(idea => idea.Symbol), Is.EqualTo(new[] { "SOLUSDT" }));
        Assert.That(withShort4h.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "SOLUSDT" }), "BTCUSDT has a short of 1h and a long of 4h, not a short of 4h");
        Assert.That((withIdeas.Total, withIdeas.Items.Select(row => row.Pair.Symbol)), Is.EqualTo((2, new[] { "BTCUSDT", "SOLUSDT" })).Using<(int, IEnumerable<string>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(fundingShort4h.Items.Select(row => row.Pair.Symbol), Is.EqualTo(new[] { "SOLUSDT" }));
        Assert.That(withIdeas.Items[0].Ideas.Select(badge => (badge.Strategy, badge.Timeframe, badge.Side)), Is.EquivalentTo(new[] { ("funding-short", "1h", "short"), ("breakout-long", "4h", "long") }));
    }

    /// <summary>
    ///     Spec saved-filters «Счётчик подходящих строк»: «Подходит двенадцать пар».
    /// </summary>
    [Test]
    public async Task The_count_tells_the_matching_pairs_and_open_ideas_of_all()
    {
        List<MarketPair> pairs = [.. Enumerable.Range(1, 40).Select(index => Pair($"P{index:D2}USDT", funding: index <= 12 ? 0.0004m : 0.0001m, turnover: 200_000_000m))];
        await _market.ReplaceAsync(pairs, CancellationToken.None);
        await Add(IdeaGenerator.Generate(symbol: "P01USDT"));
        await Add(IdeaGenerator.Generate(symbol: "P30USDT"));

        FilterCount count = await _market.CountAsync([new FilterCondition("fundingRate", "ge", "0.03"), new FilterCondition("turnover24h", "gt", "100")], CancellationToken.None);

        Assert.That(count, Is.EqualTo(new FilterCount(12, 40, 1, 2)));
    }

    /// <summary>
    ///     Spec strategy-ideas «Список идей» and «Сводка журнала» at the store: «Открытые по времени», «Стратегия и сторона»,
    ///     «Период журнала», «Три идеи» and «Исключённая идея».
    /// </summary>
    [Test]
    public async Task The_lists_of_ideas_and_the_summary_of_the_journal()
    {
        var fundingShort = Guid.NewGuid();
        Idea late = await Add(IdeaGenerator.Generate(fundingShort, symbol: "XRPUSDT", moment: Now.AddHours(-1)));
        await Add(IdeaGenerator.Generate(Guid.NewGuid(), name: "breakout-long", symbol: "SOLUSDT", side: EntrySide.Long, moment: Now.AddHours(-3)));
        Idea early = await Add(IdeaGenerator.Generate(fundingShort, symbol: "BTCUSDT", moment: Now.AddHours(-2)));
        foreach ((string symbol, double result) in new[] { ("AUSDT", 0.004), ("BUSDT", -0.002), ("CUSDT", -0.005) })
        {
            await Add(IdeaGenerator.Generate(fundingShort, symbol: symbol, timeframe: TimeSpan.FromHours(1)), closed: result);
        }

        await Add(IdeaGenerator.Generate(fundingShort, symbol: "DUSDT", timeframe: TimeSpan.FromHours(1)), excluded: true);
        await Add(IdeaGenerator.Generate(fundingShort, symbol: "EUSDT", timeframe: TimeSpan.FromHours(1)), closed: 0.01, exitDaysAgo: 30);

        ListPage<Idea> open = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50)), CancellationToken.None);
        ListPage<Idea> shorts = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Open, new ListOrder("moment", true, 1, 50), Strategy: "funding-short", Side: "short"), CancellationToken.None);
        ListPage<Idea> week = await _ideas.ListAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("exitTime", true, 1, 50), PeriodDays: 7), CancellationToken.None);
        ListPage<IdeaSummary> summary = await _ideas.SummaryAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("ideas", true, 1, 50), PeriodDays: 7), CancellationToken.None);

        Assert.That(open.Items.Select(idea => idea.Symbol), Is.EqualTo(new[] { "XRPUSDT", "BTCUSDT", "SOLUSDT" }));
        Assert.That((shorts.Total, shorts.Items.Select(idea => idea.Id)), Is.EqualTo((2, new[] { late.Id, early.Id })).Using<(int, IEnumerable<Guid>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That(week.Items.Select(idea => idea.Symbol), Is.EquivalentTo(new[] { "AUSDT", "BUSDT", "CUSDT" }), "the exit of EUSDT is 30 days back; DUSDT is excluded");
        IdeaSummary row = summary.Items.Single();
        Assert.That((row.Strategy, row.Version, row.Timeframe, row.Ideas), Is.EqualTo(("funding-short", 4, "1h", 3)));
        Assert.That((row.Wins, row.Mean), Is.EqualTo((1.0 / 3, -0.001)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-12 && Math.Abs(a.Item2 - b.Item2) < 1e-12));
        Assert.That(await _ideas.CountsAsync(CancellationToken.None), Is.EqualTo((3, 4)));
    }

    /// <summary>
    ///     Task 18.16: pages of the summary with equal values of the sort neither repeat nor lose a row — the groups of one
    ///     name and version on several timeframes and of strategies of the same name — and the journal has an index of its
    ///     state and time of exit.
    /// </summary>
    [Test]
    public async Task Pages_with_equal_values_of_the_sort_neither_repeat_nor_lose_a_row()
    {
        TimeSpan[] timeframes = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromDays(1)];
        for (int strategy = 0; strategy < 12; strategy++)
        {
            var id = Guid.NewGuid();
            foreach (TimeSpan timeframe in timeframes)
            {
                await Add(IdeaGenerator.Generate(strategy: id, name: "same-name", timeframe: timeframe), closed: 0.001);
            }
        }

        List<(Guid, int, string)> seen = [];
        for (int page = 1; page <= 9; page++)
        {
            ListPage<IdeaSummary> rows = await _ideas.SummaryAsync(new IdeaListQuery(IdeaListKind.Closed, new ListOrder("ideas", true, page, 7)), CancellationToken.None);
            seen.AddRange(rows.Items.Select(row => (row.StrategyId, row.Version, row.Timeframe)));
        }

        Assert.That(seen, Has.Count.EqualTo(60));
        Assert.That(seen.Distinct().Count(), Is.EqualTo(60), "no group twice, none lost");
    }

    /// <summary>
    ///     Task 19.6: the index of the journal is in the order of the journal — the time of exit descending with the missing
    ///     last, then its tie-break — so on 10 000 closed ideas the page of the journal is read from the index without a sort.
    /// </summary>
    [Test]
    public async Task The_journal_is_read_in_the_order_of_its_index_without_a_sort()
    {
        Idea first = await Add(IdeaGenerator.Generate(symbol: "SOLUSDT", moment: Now.AddDays(-30)), closed: 0.001);
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ideas
            SELECT (jsonb_populate_record(NULL::ideas, to_jsonb(i) || jsonb_build_object(
                'id', gen_random_uuid(),
                'moment', i.moment + make_interval(mins => g),
                'exit_time', CASE WHEN g % 10 = 0 THEN NULL ELSE now() - make_interval(mins => g) END))).*
            FROM ideas i, generate_series(1, 9999) g
            WHERE i.id = @Id
            """,
            new { first.Id });
        await connection.ExecuteAsync("ANALYZE ideas");

        string[] plan = [.. await connection.QueryAsync<string>(
            """
            EXPLAIN SELECT i.* FROM ideas i LEFT JOIN strategies s ON s.id = i.strategy_id LEFT JOIN market_pairs m ON m.symbol = i.symbol
            WHERE i.state = 'closed' AND i.exit_time >= now() - make_interval(days => 7) AND TRUE AND TRUE
            ORDER BY i.exit_time DESC NULLS LAST, i.moment DESC, i.id LIMIT 50 OFFSET 50
            """)];
        Assert.That(plan, Has.Some.Contains("ideas_state_exit_time_ix"), string.Join('\n', plan));
        Assert.That(plan, Has.None.Contains("Sort"), string.Join('\n', plan));
        Assert.That(
            await Scalar<string>("SELECT indexdef FROM pg_indexes WHERE tablename = 'ideas' AND indexname = 'ideas_state_exit_time_ix'"),
            Does.EndWith("(state, exit_time DESC NULLS LAST, moment DESC, id)"));
    }

    private async Task<Idea> Add(Idea idea, double? closed = null, bool excluded = false, int exitDaysAgo = 1)
    {
        await _ideas.AddAsync(idea, CancellationToken.None);
        if (closed is { } result)
        {
            idea.State = IdeaState.Closed;
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-exitDaysAgo);
            idea.ExitReason = ExitReason.Time;
            idea.Result = result;
            idea.ResultUsdt = result * 100;
            await _ideas.SaveAsync(idea, CancellationToken.None);
        }
        else if (excluded)
        {
            idea.State = IdeaState.Excluded;
            idea.Excluded = "нет данных";
            idea.ExitTime = DateTimeOffset.UtcNow.AddDays(-1);
            await _ideas.SaveAsync(idea, CancellationToken.None);
        }

        return idea;
    }

    private static MarketPair Pair(string symbol, decimal? change24h = 0m, decimal funding = 0.0001m, decimal turnover = 1_000_000m)
    {
        return new MarketPair(symbol, 1, 0, 0, change24h, turnover, 1, 1, funding, null, Now);
    }

    private static async Task<T?> Scalar<T>(string sql)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql);
    }
}
