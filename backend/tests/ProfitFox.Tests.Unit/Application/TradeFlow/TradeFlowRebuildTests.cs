using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.TradeFlow;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>Requirements "Пересборка дня из архива", "Учёт пересобранных дней" and "История потока сделок из архива" (decision 8).</summary>
public class TradeFlowRebuildTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly Guid Owner = Guid.NewGuid();

    private FakeTimeProvider _time = null!;
    private ITradeArchive _archive = null!;
    private ITradeFlowMinuteRepository _minutes = null!;
    private ITradeFlowDayRepository _days = null!;
    private ITradeFlowJournal _journal = null!;
    private List<(Instrument Instrument, DateTimeOffset Day)> _asked = null!;
    private Dictionary<(Instrument, DateTimeOffset), TradeEvent[]> _files = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-10T03:00:00Z", CultureInfo.InvariantCulture));
        _asked = [];
        _files = [];
        _archive = Substitute.For<ITradeArchive>();
        _archive.ReadDayAsync(Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<Action<TradeEvent>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                (Instrument, DateTimeOffset) key = (call.ArgAt<Instrument>(0), call.ArgAt<DateTimeOffset>(1));
                _asked.Add(key);

                if (!_files.TryGetValue(key, out TradeEvent[]? trades))
                {
                    return false;
                }

                foreach (TradeEvent trade in trades)
                {
                    call.ArgAt<Action<TradeEvent>>(2)(trade);
                }

                return true;
            });
        _minutes = Substitute.For<ITradeFlowMinuteRepository>();
        _minutes.ReplaceDayAsync(Owner, Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<TradeFlowMinute>>(), Arg.Any<CancellationToken>()).Returns(true);
        _days = Substitute.For<ITradeFlowDayRepository>();
        _days.GetAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _journal = Substitute.For<ITradeFlowJournal>();
    }

    // Scenario "Ночная пересборка": the minutes of the day are those of the file, written whole, and the day is recorded rebuilt.
    [Test]
    public async Task Closed_day_is_replaced_with_the_minutes_of_its_file_and_recorded_rebuilt()
    {
        _files[(Sol, Day)] =
        [
            new TradeEvent(Day.AddHours(14).AddMinutes(2).AddSeconds(1), TradeSide.Buy, 2m, 150m),
            new TradeEvent(Day.AddHours(14).AddMinutes(2).AddSeconds(30), TradeSide.Sell, 1m, 149m),
            new TradeEvent(Day.AddHours(14).AddMinutes(5), TradeSide.Buy, 1m, 151m)
        ];

        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);

        await _minutes.Received(1).ReplaceDayAsync(
            Owner,
            Sol,
            Day,
            Arg.Is<IReadOnlyCollection<TradeFlowMinute>>(minutes => minutes.OrderBy(minute => minute.OpenTime).SequenceEqual(new[]
            {
                new TradeFlowMinute(Day.AddHours(14).AddMinutes(2), 2m, 1m, 300m, 149m, 1, 1),
                new TradeFlowMinute(Day.AddHours(14).AddMinutes(5), 1m, 0m, 151m, 0m, 1, 0)
            })),
            Arg.Any<CancellationToken>());
        await _days.Received(1).SaveAsync(new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Rebuilt, _time.GetUtcNow()), Arg.Any<CancellationToken>());
        _journal.Received(1).Rebuilt(Sol, Day, 3, Arg.Any<TimeSpan>());
    }

    [Test]
    public async Task Day_is_not_asked_for_before_the_archive_comes_out()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-10T02:29:59Z", CultureInfo.InvariantCulture));

        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);
        _time.SetUtcNow(DateTimeOffset.Parse("2026-11-10T02:30:00Z", CultureInfo.InvariantCulture));
        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);

        _asked.ShouldBe([(Sol, Day)]);
    }

    // Scenario "Архив ещё не вышел": the day is left as it is and asked for again at the next pass.
    [Test]
    public async Task Day_without_its_file_yet_stays_in_the_queue()
    {
        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));
        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);

        _asked.ShouldBe([(Sol, Day), (Sol, Day)]);
        await _minutes.DidNotReceiveWithAnyArgs().ReplaceDayAsync(default, null!, default, null!, default);
        await _days.DidNotReceiveWithAnyArgs().SaveAsync(null!, default);
    }

    // Scenario "Пара без файла": 3 days after the day no file is final; the day is recorded missing and its minutes are left.
    [Test]
    public async Task Day_without_a_file_3_days_after_it_is_recorded_missing()
    {
        _time.SetUtcNow(DateTimeOffset.Parse("2026-11-12T23:59:59Z", CultureInfo.InvariantCulture));
        await Rebuild(historyDays: 3).RunAsync([Sol], CancellationToken.None);
        _time.SetUtcNow(DateTimeOffset.Parse("2026-11-13T00:00:00Z", CultureInfo.InvariantCulture));
        await Rebuild(historyDays: 4).RunAsync([Sol], CancellationToken.None);

        await _days.Received(1).SaveAsync(Arg.Any<TradeFlowDay>(), Arg.Any<CancellationToken>());
        await _days.Received(1).SaveAsync(new TradeFlowDay(Sol, Day, TradeFlowDayStatus.Missing, _time.GetUtcNow()), Arg.Any<CancellationToken>());
        await _minutes.DidNotReceiveWithAnyArgs().ReplaceDayAsync(default, null!, default, null!, default);
    }

    // Scenario "Повтор пересборки": the same file gives the same minutes.
    [Test]
    public async Task Day_rebuilt_again_from_the_same_file_gives_the_same_minutes()
    {
        _files[(Sol, Day)] = [new TradeEvent(Day.AddHours(1), TradeSide.Buy, 2m, 150m), new TradeEvent(Day.AddHours(1).AddSeconds(5), TradeSide.Sell, 1m, 151m)];
        var written = new List<TradeFlowMinute[]>();
        _minutes.ReplaceDayAsync(Owner, Sol, Day, Arg.Do<IReadOnlyCollection<TradeFlowMinute>>(minutes => written.Add([.. minutes])), Arg.Any<CancellationToken>()).Returns(true);

        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);
        await Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None);

        written.Count.ShouldBe(2);
        written[1].ShouldBe(written[0]);
    }

    // Scenario "Первый запуск": the last N closed days of every pair, the freshest first.
    [Test]
    public async Task First_pass_rebuilds_the_last_days_of_every_pair_the_freshest_first()
    {
        _time.SetUtcNow(DateTimeOffset.Parse("2026-11-11T03:00:00Z", CultureInfo.InvariantCulture));

        await Rebuild(historyDays: 30).RunAsync([Sol, Btc], CancellationToken.None);

        _asked.Count.ShouldBe(60);
        _asked.Take(2).ShouldBe([(Btc, Day.AddDays(1)), (Sol, Day.AddDays(1))], ignoreOrder: true);
        _asked.Skip(58).ShouldBe([(Btc, Day.AddDays(-28)), (Sol, Day.AddDays(-28))], ignoreOrder: true);
        _asked.Select(item => item.Day).ShouldBeInOrder(SortDirection.Descending);
    }

    // Scenario "Число дней увеличили": only the days storage has no record of are rebuilt.
    [Test]
    public async Task Days_already_recorded_are_not_rebuilt_again()
    {
        _time.SetUtcNow(DateTimeOffset.Parse("2026-11-11T03:00:00Z", CultureInfo.InvariantCulture));
        DateTimeOffset today = Day.AddDays(2);
        _days.GetAsync(today.AddDays(-45), today, Arg.Any<CancellationToken>())
            .Returns([.. Enumerable.Range(1, 30).Select(i => new TradeFlowDay(Sol, today.AddDays(-i), i == 30 ? TradeFlowDayStatus.Missing : TradeFlowDayStatus.Rebuilt, _time.GetUtcNow()))]);

        await Rebuild(historyDays: 45).RunAsync([Sol], CancellationToken.None);

        _asked.Select(item => item.Day).ShouldBe([.. Enumerable.Range(31, 15).Select(i => today.AddDays(-i))]);
    }

    // Decision 2: a collector that lost the lease writes nothing more.
    [Test]
    public async Task Day_refused_for_the_lease_ends_the_pass()
    {
        _files[(Sol, Day)] = [new TradeEvent(Day.AddHours(1), TradeSide.Buy, 2m, 150m)];
        _minutes.ReplaceDayAsync(Owner, Arg.Any<Instrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<TradeFlowMinute>>(), Arg.Any<CancellationToken>()).Returns(false);

        await Should.ThrowAsync<TradeFlowLeaseLostException>(() => Rebuild(historyDays: 1).RunAsync([Sol], CancellationToken.None));

        await _days.DidNotReceiveWithAnyArgs().SaveAsync(null!, default);
    }

    private TradeFlowRebuild Rebuild(int historyDays)
    {
        return new TradeFlowRebuild(_archive, _minutes, _days, new TradeFlowSettings { HistoryDays = historyDays }, new WorkerIdentity(Owner), _journal, _time);
    }
}