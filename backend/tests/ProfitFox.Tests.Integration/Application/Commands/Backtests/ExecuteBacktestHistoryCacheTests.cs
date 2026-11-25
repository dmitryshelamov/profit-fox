using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Infrastructure.Clients.MarketData.Protos;
using ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Backtests;
using ProfitFox.Tests.Shared.Logging;

using static ProfitFox.Tests.Integration.Infrastructure.Persistence.Files.HistoryCache.HistoryCacheFolder;

namespace ProfitFox.Tests.Integration.Application.Commands.Backtests;

/// <summary>
///     Change add-backtest-history-cache, tasks 4.2, 4.5 and 4.6: backtests of the queue on the fake market-data service with
///     a folder of the cache of history, against those of a Worker without it — spec market-history-cache «Кэш истории на
///     диске процесса», «Тот же ответ, что у сервиса данных», «Сбой файла или папки кэша» and «Счётчики кэша в журнале
///     прогона», spec market-history «Чтение через кэш» and «Минутные свечи в окнах сделок», spec strategy-backtest «История
///     прогона». The history of the fake starts on 2026-01-05, so January 2026 is the month of the listing and the months
///     before it are not covered; the clock is that of the machine, after every end here.
/// </summary>
[NonParallelizable]
public sealed class ExecuteBacktestHistoryCacheTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset February = Day(2026, 2, 1);
    private MarketDataServer _server = null!;
    private HistoryCacheFolder _folder = null!;
    private readonly List<ServiceProvider> _services = [];

    [SetUp]
    public async Task Start()
    {
        FakeMarketData.CoveredUntil = null;
        await BenchDatabase.Reset();
        _server = await MarketDataServer.Start();
        _folder = new HistoryCacheFolder();
    }

    [TearDown]
    public async Task Stop()
    {
        foreach (ServiceProvider services in _services)
        {
            await services.DisposeAsync();
        }

        await _server.DisposeAsync();
        _folder.Dispose();
    }

    /// <summary>
    ///     «Повторный прогон», «Второй прогон», «Прогон с кэшем и без» of market-history-cache, «Повторный прогон», «Месяц шире
    ///     окна» and «Месяц без окон» of market-history.
    /// </summary>
    [Test]
    public async Task A_second_backtest_reads_no_closed_whole_month_and_has_the_hash_and_the_numbers_of_one_without_the_cache()
    {
        DateTimeOffset end = Day(2026, 3, 16);
        BacktestOutcome plain = await Run(Plain(), Generate(end));
        _server.Data.CandleRequests.Clear();
        _server.Data.FundingRequests.Clear();
        ServiceProvider cached = Cached();

        BacktestOutcome first = await Run(cached, Generate(end));
        (DateTimeOffset From, DateTimeOffset To)[] minutes = [.. Minutes().Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset()))];
        _server.Data.CandleRequests.Clear();
        _server.Data.FundingRequests.Clear();
        BacktestOutcome second = await Run(cached, Generate(end));

        Assert.That(minutes, Has.Some.Matches<(DateTimeOffset From, DateTimeOffset To)>(window => window.From <= Day(2026, 3, 1) && window.To == Day(2026, 4, 1)), "the whole of March, after the end, for the weeks of March the backtest asked");
        Assert.That(minutes.Select(window => window.To), Has.All.LessThanOrEqualTo(Day(2026, 4, 1)), "no window touches April: no minute of it is asked");
        Assert.That(_folder.Months.Select(path => Path.GetRelativePath(_folder.Path, path)), Does.Contain(Path.Combine("btc", "candles-1m", "2026-03.gz")).And.Contain(Path.Combine("btc", "candles-4h", "2026-02.gz")).And.Contain(Path.Combine("btc", "funding", "2026-03.gz")));
        Assert.That(_server.Data.CandleRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(February), "the second backtest asks only what is not covered whole: the listing and before");
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(February));
        Assert.That(first.DataHash, Is.EqualTo(plain.DataHash));
        Assert.That(second.DataHash, Is.EqualTo(plain.DataHash));
        Assert.That(Numbers(first), Is.EqualTo(Numbers(plain)));
        Assert.That(Numbers(second), Is.EqualTo(Numbers(plain)));
    }

    /// <summary>
    ///     A new process with the same folder takes the months of the first one.
    /// </summary>
    [Test]
    public async Task A_Worker_started_again_with_the_same_folder_reads_the_closed_whole_months_from_it()
    {
        DateTimeOffset end = Day(2026, 3, 16);
        BacktestOutcome first = await Run(Cached(), Generate(end));
        _server.Data.CandleRequests.Clear();
        _server.Data.FundingRequests.Clear();

        BacktestOutcome again = await Run(Cached(), Generate(end));

        Assert.That(_server.Data.CandleRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(February));
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(February));
        Assert.That(again.DataHash, Is.EqualTo(first.DataHash));
    }

    /// <summary>
    ///     «Неделя без событий» of market-history: with no orders no minute is asked, with the cache or without.
    /// </summary>
    [Test]
    public async Task A_backtest_without_orders_asks_no_minutes_with_the_cache_or_without()
    {
        DateTimeOffset end = Day(2026, 3, 16);

        BacktestOutcome plain = await Run(Plain(), Generate(end, thresholds: ["0.01"]));
        int plainMinutes = Minutes().Count();
        BacktestOutcome cached = await Run(Cached(), Generate(end, thresholds: ["0.01"]));

        Assert.That(plain.Sets.Single().Numbers.Trades, Is.Zero);
        Assert.That(plainMinutes, Is.Zero);
        Assert.That(Minutes(), Is.Empty);
        Assert.That(cached.DataHash, Is.EqualTo(plain.DataHash));
    }

    /// <summary>
    ///     «Шесть наборов на одной истории» of market-history with the cache: each part of the history of a pair is asked once.
    /// </summary>
    [Test]
    public async Task Six_sets_with_the_cache_ask_each_part_of_the_history_of_a_pair_once()
    {
        await Run(Cached(), Generate(Day(2026, 3, 16), thresholds: ["0.0003", "0.0004", "0.0005", "0.00055", "0.0006", "0.001"]));

        CandleHistoryRequest[] btc = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "btc")];
        Assert.That(btc.Count(request => request.Interval == CandleInterval.FourHours), Is.EqualTo(1));
        (DateTimeOffset From, DateTimeOffset To)[] minutes = [.. btc.Where(request => request.Interval != CandleInterval.FourHours).Select(request => (request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset())).OrderBy(window => window.Item1)];
        Assert.That(minutes, Is.Not.Empty);
        Assert.That(minutes, Is.Unique, "each window once for the six sets");
        // From February on every month is covered whole and kept: each minute of it is asked once. January, the month of the
        // listing, is asked whole by its first week and by the window of each week after (decision 4).
        (DateTimeOffset From, DateTimeOffset To)[] kept = [.. minutes.Where(window => window.To > February)];
        Assert.That(kept.Zip(kept.Skip(1)).Select(pair => pair.First.To <= pair.Second.From), Has.All.True, "no minute of a whole month is asked twice");
        Assert.That(_server.Data.FundingRequests.Count(request => request.InstrumentIds.Single() == "btc"), Is.EqualTo(1));
    }

    /// <summary>
    ///     «История после конца» of strategy-backtest: a backtest that ends on 2026-06-01 gets nothing of June, with the cache
    ///     or without; without it, nothing after the end is asked.
    /// </summary>
    [Test]
    public async Task A_backtest_gets_nothing_after_its_end_with_the_cache_or_without()
    {
        DateTimeOffset end = Day(2026, 6, 1);
        BacktestOutcome plain = await Run(Plain(), Generate(end));
        Assert.That(_server.Data.CandleRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(end));
        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Has.All.LessThanOrEqualTo(end.AddSeconds(1)), "the payment at the end");
        _server.Data.FundingRequests.Clear();

        BacktestOutcome cached = await Run(Cached(), Generate(end));

        Assert.That(_server.Data.FundingRequests.Select(request => request.To.ToDateTimeOffset()), Does.Contain(Day(2026, 7, 1)), "June of funding is read whole for the payment at the end");
        Assert.That(cached.DataHash, Is.EqualTo(plain.DataHash), "nothing of June went into the hash");
        Assert.That(Numbers(cached), Is.EqualTo(Numbers(plain)));
    }

    /// <summary>
    ///     «Конец в середине месяца» of strategy-backtest: May is read whole and kept, and nothing from the end on is in the
    ///     backtest.
    /// </summary>
    [Test]
    public async Task A_backtest_that_ends_in_the_middle_of_a_month_reads_and_keeps_the_whole_month_and_gets_nothing_after_its_end()
    {
        DateTimeOffset end = Day(2026, 5, 16);
        BacktestOutcome plain = await Run(Plain(), Generate(end));
        _server.Data.CandleRequests.Clear();

        BacktestOutcome cached = await Run(Cached(), Generate(end));

        Assert.That(_server.Data.CandleRequests.Where(request => request.Interval == CandleInterval.FourHours).Select(request => request.To.ToDateTimeOffset()), Has.All.EqualTo(Day(2026, 6, 1)));
        Assert.That(File.Exists(Path.Combine(_folder.Path, "btc", "candles-4h", "2026-05.gz")), Is.True);
        Assert.That(cached.DataHash, Is.EqualTo(plain.DataHash));
        Assert.That(Numbers(cached), Is.EqualTo(Numbers(plain)));
    }

    /// <summary>
    ///     A backtest whose read is dropped twice by the service ends with the hash of one without drops, and the months kept
    ///     before the drops are not asked again: the repeat comes into the cache anew.
    /// </summary>
    [Test]
    public async Task A_backtest_with_two_drops_ends_with_the_same_hash_and_does_not_ask_again_the_months_kept_before_them()
    {
        DateTimeOffset end = Day(2026, 5, 16);
        BacktestOutcome plain = await Run(Plain(), Generate(end));
        ServiceProvider cached = Cached();
        // March of 4h of BTCUSDT is in the cache: the read of the backtest asks the service for what is before it and after it.
        await cached.GetRequiredService<IHistorySource>().ReadCandles("btc", TimeSpan.FromHours(4), Day(2026, 3, 1), Day(2026, 4, 1), CancellationToken.None);
        _server.Data.CandleRequests.Clear();
        int dropped = 0;
        _server.Data.BeforeCandles = (request, _) =>
        {
            if (request.InstrumentIds.Single() == "btc" && request.Interval == CandleInterval.FourHours && request.From.ToDateTimeOffset() == Day(2026, 4, 1) && Interlocked.Increment(ref dropped) <= 2)
            {
                _server.Data.Drops = 1;
            }

            return Task.CompletedTask;
        };

        BacktestOutcome repeated = await Run(cached, Generate(end));

        CandleHistoryRequest[] fourHours = [.. _server.Data.CandleRequests.Where(request => request.InstrumentIds.Single() == "btc" && request.Interval == CandleInterval.FourHours)];
        Assert.That(dropped, Is.EqualTo(3), "the part after March was asked three times: two drops and the read");
        Assert.That(fourHours.Count(request => request.From.ToDateTimeOffset() <= February && request.To.ToDateTimeOffset() > February), Is.EqualTo(1), "February, kept before the drops, is asked once");
        Assert.That(repeated.DataHash, Is.EqualTo(plain.DataHash));
        Assert.That(Numbers(repeated), Is.EqualTo(Numbers(plain)));
    }

    /// <summary>
    ///     «Журнал повторного прогона» of market-history-cache, and the line of a Worker without the folder as it was.
    /// </summary>
    [Test]
    public async Task The_line_of_the_end_of_a_backtest_tells_the_counters_of_the_cache_and_is_as_before_without_it()
    {
        DateTimeOffset end = Day(2026, 3, 16);
        var plainLog = new ListLogger();
        var cachedLog = new ListLogger();
        ServiceProvider cached = Cached(cachedLog);
        await Run(Plain(plainLog), Generate(end));
        await Run(cached, Generate(end));
        cachedLog.Entries.Clear();

        await Run(cached, Generate(end));

        string plainLine = plainLog.Entries.Single(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("Backtest ", StringComparison.Ordinal)).Message;
        string cachedLine = cachedLog.Entries.Single(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("Backtest ", StringComparison.Ordinal)).Message;
        Assert.That(plainLine, Does.Match(@"^Backtest [0-9a-f-]+: 3 pairs, 1 sets, read [0-9.,]+ s, computed [0-9.,]+ s$"));
        Assert.That(cachedLine, Does.Match(@"^Backtest [0-9a-f-]+: 3 pairs, 1 sets, read [0-9.,]+ s, computed [0-9.,]+ s, cache: [1-9][0-9]* months from the cache, 0 saved, [0-9]+ requests to the service$"));
    }

    /// <summary>
    ///     «В папку нельзя писать» of market-history-cache: the backtest ends with the hash and the numbers of one without the
    ///     cache, and the journal warns of the cache.
    /// </summary>
    [Test]
    public async Task A_backtest_whose_folder_of_the_cache_cannot_be_written_ends_as_one_without_the_cache_with_a_warning()
    {
        DateTimeOffset end = Day(2026, 3, 16);
        BacktestOutcome plain = await Run(Plain(), Generate(end));
        Directory.CreateDirectory(Path.GetDirectoryName(_folder.Path)!);
        await File.WriteAllTextAsync(_folder.Path, "not a folder");
        var log = new ListLogger();

        try
        {
            BacktestOutcome cached = await Run(Cached(log), Generate(end));

            Assert.That(cached.DataHash, Is.EqualTo(plain.DataHash));
            Assert.That(Numbers(cached), Is.EqualTo(Numbers(plain)));
            Assert.That(log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message), Has.Some.Contains("cache of history"));
        }
        finally
        {
            File.Delete(_folder.Path);
        }
    }

    private IEnumerable<CandleHistoryRequest> Minutes()
    {
        return _server.Data.CandleRequests.Where(request => request.Interval == CandleInterval.OneMinute);
    }

    private ServiceProvider Plain(ListLogger? log = null)
    {
        ServiceProvider services = BenchServices.Start(_server, log: log);
        _services.Add(services);
        return services;
    }

    private ServiceProvider Cached(ListLogger? log = null)
    {
        ServiceProvider services = BenchServices.Start(_server, cache: _folder.Path, log: log);
        _services.Add(services);
        return services;
    }

    private static Backtest Generate(DateTimeOffset end, IReadOnlyList<string>? thresholds = null)
    {
        Backtest backtest = BacktestGenerator.Generate(thresholds: thresholds);
        return new Backtest
        {
            Id = backtest.Id,
            StrategyId = backtest.StrategyId,
            StrategyName = backtest.StrategyName,
            StrategyVersion = backtest.StrategyVersion,
            Kind = backtest.Kind,
            Graph = backtest.Graph,
            Settings = backtest.Settings with { End = end },
            Sets = backtest.Sets,
            SetCount = backtest.SetCount
        };
    }

    // The numbers of the sets and of their pairs as text: equal text, equal numbers.
    private static string Numbers(BacktestOutcome outcome)
    {
        return JsonSerializer.Serialize(outcome.Sets.Select(set => new { set.Set, set.Numbers, set.Pairs }));
    }

    private static async Task<BacktestOutcome> Run(ServiceProvider services, Backtest backtest)
    {
        IBacktestRepository backtests = services.GetRequiredService<IBacktestRepository>();
        await backtests.EnqueueAsync(backtest, CancellationToken.None);
        Backtest claimed = (await backtests.ClaimAsync("w-1", Lease, CancellationToken.None))!;
        ExecuteBacktestResult result = await services.GetRequiredService<ISender>().Send(new ExecuteBacktestCommand(claimed, new RunAssignment(claimed.Id, "w-1")));
        Assert.That((await backtests.FindAsync(claimed.Id, CancellationToken.None))!.Status, Is.EqualTo(BacktestState.Done));
        return result.Outcome;
    }
}
