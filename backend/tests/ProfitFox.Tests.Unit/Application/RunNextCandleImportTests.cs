using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Commands.RunNextCandleImport;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

public class RunNextCandleImportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private FakeTimeProvider _time = null!;
    private IMarketHistoryClient _marketHistory = null!;
    private ICandleImportJobRepository _jobs = null!;
    private ICandleRepository _candles = null!;
    private RunNextCandleImportCommandHandler _handler = null!;
    private readonly WorkerIdentity _worker = new WorkerIdentity(Guid.NewGuid());

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Now);
        _marketHistory = Substitute.For<IMarketHistoryClient>();
        _jobs = Substitute.For<ICandleImportJobRepository>();
        _candles = Substitute.For<ICandleRepository>();
        _candles.GetOpenTimesAsync(
                Arg.Any<Instrument>(),
                Arg.Any<Interval>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        _jobs.UpdateLeasedAsync(Arg.Any<CandleImportJob>(), _worker.Id, Arg.Any<CancellationToken>()).Returns(true);
        _jobs.RenewLeaseAsync(Arg.Any<Guid>(), _worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        _handler = new RunNextCandleImportCommandHandler(_marketHistory, _jobs, _candles, new WorkerSettings(), _worker, _time);
    }

    [Test]
    public async Task Job_is_running_from_the_start_of_execution()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");

        await RunAsync();

        SavedStates()[0].ShouldBe(job with { Status = CandleImportStatus.Running, StartedAt = Now });
    }

    // Scenarios "Успешное завершение" and "Границы периода".
    [Test]
    public async Task Stores_candles_of_the_period_and_succeeds()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(
            job,
            job.From,
            job.To,
            () => Candles("2024-01-01T03:00:00Z", "2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z"),
            () => Candles("2024-01-01T00:00:00Z", "2023-12-31T23:00:00Z"));

        await RunAsync();

        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z", "2024-01-01T00:00:00Z"]);
        SavedStates()[^1].ShouldBe(job with { Status = CandleImportStatus.Succeeded, ImportedCandles = 3, StartedAt = Now, FinishedAt = Now });
    }

    // Scenario "Период включает текущий момент".
    [Test]
    public async Task Drops_the_candle_that_is_not_closed_yet()
    {
        CandleImportJob job = PendingJob("2024-03-10T10:00:00Z", "2024-03-10T12:30:00Z");
        ServePages(job, job.From, job.To, () => Candles("2024-03-10T12:00:00Z", "2024-03-10T11:00:00Z", "2024-03-10T10:00:00Z"));

        await RunAsync();

        StoredOpenTimes().ShouldBe(["2024-03-10T11:00:00Z", "2024-03-10T10:00:00Z"]);
    }

    [Test]
    public async Task Progress_is_saved_after_each_page()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        long importedBeforeSecondPage = -1;
        ServePages(
            job,
            job.From,
            job.To,
            () => Candles("2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z"),
            () =>
            {
                importedBeforeSecondPage = SavedStates()[^1].ImportedCandles;

                return Candles("2024-01-01T00:00:00Z");
            });

        await RunAsync();

        importedBeforeSecondPage.ShouldBe(2);
    }

    // Scenario "Нет данных за весь период".
    [Test]
    public async Task Period_before_listing_succeeds_with_no_candles()
    {
        CandleImportJob job = PendingJob("2010-01-01T00:00:00Z", "2010-02-01T00:00:00Z");

        await RunAsync();

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Succeeded);
        SavedStates()[^1].ImportedCandles.ShouldBe(0);
    }

    // Scenario "Период загружен частично другой задачей".
    [Test]
    public async Task Requests_only_the_ranges_without_stored_candles()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T04:00:00Z");
        Stored(job, "2024-01-01T01:00:00Z");
        ServePages(job, At("2024-01-01T00:00:00Z"), At("2024-01-01T01:00:00Z"), () => Candles("2024-01-01T00:00:00Z"));
        ServePages(job, At("2024-01-01T02:00:00Z"), At("2024-01-01T04:00:00Z"), () => Candles("2024-01-01T03:00:00Z", "2024-01-01T02:00:00Z"));

        await RunAsync();

        RequestedRanges()
            .ShouldBe([("2024-01-01T00:00:00Z", "2024-01-01T01:00:00Z"), ("2024-01-01T02:00:00Z", "2024-01-01T04:00:00Z")]);
        StoredOpenTimes().ShouldBe(["2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z", "2024-01-01T02:00:00Z"]);
        SavedStates()[^1].ShouldBe(job with { Status = CandleImportStatus.Succeeded, ImportedCandles = 3, StartedAt = Now, FinishedAt = Now });
    }

    // Scenario "Период уже загружен целиком".
    [Test]
    public async Task Fully_stored_period_succeeds_without_asking_the_exchange()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        Stored(job, "2024-01-01T00:00:00Z", "2024-01-01T01:00:00Z", "2024-01-01T02:00:00Z");

        await RunAsync();

        RequestedRanges().ShouldBeEmpty();
        SavedStates()[^1].ShouldBe(job with { Status = CandleImportStatus.Succeeded, StartedAt = Now, FinishedAt = Now });
    }

    // Scenario "Незакрытая свеча в конце периода".
    [Test]
    public async Task Tail_with_the_open_candle_is_requested_once_and_nothing_is_stored()
    {
        CandleImportJob job = PendingJob("2024-03-10T10:00:00Z", "2024-03-10T12:30:00Z");
        Stored(job, "2024-03-10T10:00:00Z", "2024-03-10T11:00:00Z");
        ServePages(job, At("2024-03-10T12:00:00Z"), job.To, () => Candles("2024-03-10T12:00:00Z"));

        await RunAsync();

        RequestedRanges().ShouldBe([("2024-03-10T12:00:00Z", "2024-03-10T12:30:00Z")]);
        StoredOpenTimes().ShouldBeEmpty();
        SavedStates()[^1].ShouldBe(job with { Status = CandleImportStatus.Succeeded, StartedAt = Now, FinishedAt = Now });
    }

    // A stored candle of the period may come back with a page of a neighbouring range; it must stay as it is.
    [Test]
    public async Task Candle_outside_the_missing_range_is_neither_stored_nor_counted()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        Stored(job, "2024-01-01T01:00:00Z");
        ServePages(job, At("2024-01-01T02:00:00Z"), job.To, () => Candles("2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z"));

        await RunAsync();

        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z"]);
        SavedStates()[^1].ImportedCandles.ShouldBe(1);
    }

    // Scenarios "Ошибка биржи" and "Окончательный отказ биржи".
    [Test]
    public async Task Exchange_rejection_fails_the_job_at_once_and_keeps_saved_candles()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(
            job,
            job.From,
            job.To,
            () => Candles("2024-01-01T02:00:00Z"),
            () => throw new ExchangeException("params error"));

        await RunAsync();

        SavedStates()[^1]
            .ShouldBe(
                job with
                {
                    Status = CandleImportStatus.Failed,
                    ImportedCandles = 1,
                    Error = "params error",
                    StartedAt = Now,
                    FinishedAt = Now
                });
        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z"]);
        RequestedRanges().Count.ShouldBe(1);
    }

    // Scenario "Биржа восстановилась в пределах пауз".
    [Test]
    public async Task Unavailable_exchange_is_retried_after_a_pause_from_where_the_job_stopped()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        _candles.GetOpenTimesAsync(job.Instrument, job.Interval, job.From, job.To, Arg.Any<CancellationToken>())
            .Returns([], [At("2024-01-01T02:00:00Z")]);
        var requestTimes = new List<DateTimeOffset>();
        ServeAttempts(
            job,
            requestTimes,
            [() => Candles("2024-01-01T02:00:00Z"), () => throw Unavailable()],
            [() => Candles("2024-01-01T01:00:00Z", "2024-01-01T00:00:00Z")]);

        await RunAsync();

        RequestedRanges()
            .ShouldBe([("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z"), ("2024-01-01T00:00:00Z", "2024-01-01T02:00:00Z")]);
        requestTimes.ShouldBe([Now, Now.AddSeconds(1)]);
        SavedStates().Skip(1).SkipLast(1).ShouldAllBe(state => state.Status == CandleImportStatus.Running);
        SavedStates()[^1]
            .ShouldBe(job with { Status = CandleImportStatus.Succeeded, ImportedCandles = 3, StartedAt = Now, FinishedAt = Now.AddSeconds(1) });
    }

    // Scenario "Биржа недоступна дольше всех пауз".
    [Test]
    public async Task Job_fails_when_the_exchange_stays_unavailable_after_five_pauses()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        var requestTimes = new List<DateTimeOffset>();
        ServeAttempts(job, requestTimes, Enumerable.Repeat<Func<IReadOnlyList<Candle>>[]>([() => throw Unavailable()], 6).ToArray());

        await RunAsync();

        requestTimes.Select(time => (time - Now).TotalSeconds).ShouldBe([0d, 1, 6, 21, 51, 111]);
        SavedStates()[^1]
            .ShouldBe(job with { Status = CandleImportStatus.Failed, Error = Unavailable().Message, StartedAt = Now, FinishedAt = Now.AddSeconds(111) });
    }

    // Scenario "Разрозненные сбои".
    [Test]
    public async Task Saved_candle_starts_the_pauses_over()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        Func<IReadOnlyList<Candle>>[] progressThenOutage = [() => Candles("2024-01-01T02:00:00Z"), () => throw Unavailable()];
        ServeAttempts(job, [], Enumerable.Repeat(progressThenOutage, 7).Append([]).ToArray());

        await RunAsync();

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Succeeded);
        RequestedRanges().Count.ShouldBe(8);
    }

    // The open candle is dropped, so the page stores nothing and the job has not moved on.
    [Test]
    public async Task Page_without_a_stored_candle_does_not_start_the_pauses_over()
    {
        CandleImportJob job = PendingJob("2024-03-10T10:00:00Z", "2024-03-10T12:30:00Z");
        Func<IReadOnlyList<Candle>>[] openCandleThenOutage = [() => Candles("2024-03-10T12:00:00Z"), () => throw Unavailable()];
        ServeAttempts(job, [], Enumerable.Repeat(openCandleThenOutage, 6).ToArray());

        await RunAsync();

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Failed);
        RequestedRanges().Count.ShouldBe(6);
    }

    // Scenario "Задача с продвижением".
    [Test]
    public async Task Stored_candle_clears_the_lost_leases()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z", lostLeases: 2);
        ServePages(job, job.From, job.To, () => Candles("2024-01-01T02:00:00Z"));

        await RunAsync();

        SavedStates()[1].ShouldBe(job with { Status = CandleImportStatus.Running, ImportedCandles = 1, StartedAt = Now, LostLeases = 0 });
    }

    [Test]
    public async Task Page_without_a_stored_candle_keeps_the_lost_leases()
    {
        CandleImportJob job = PendingJob("2024-03-10T10:00:00Z", "2024-03-10T12:30:00Z", lostLeases: 2);
        ServePages(job, job.From, job.To, () => Candles("2024-03-10T12:00:00Z"));

        await RunAsync();

        SavedStates()[1].LostLeases.ShouldBe(2);
    }

    [Test]
    public async Task Cancellation_leaves_the_job_running()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        using var cancellation = new CancellationTokenSource();
        ServePages(
            job,
            job.From,
            job.To,
            () =>
            {
                cancellation.Cancel();

                throw new OperationCanceledException(cancellation.Token);
            });

        await Should.ThrowAsync<OperationCanceledException>(() => _handler.Handle(new RunNextCandleImportCommand(), cancellation.Token));

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Running);
    }

    // A shutdown between storing a page and saving the job would otherwise lose the count for good.
    [Test]
    public async Task Progress_of_a_stored_page_is_saved_even_when_cancelled_meanwhile()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        using var cancellation = new CancellationTokenSource();
        ServePages(job, job.From, job.To, () => Candles("2024-01-01T02:00:00Z"));
        _candles.When(candles => candles.UpsertAsync(
                Arg.Any<Instrument>(),
                Arg.Any<Interval>(),
                Arg.Any<IReadOnlyCollection<Candle>>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => cancellation.Cancel());

        await _handler.Handle(new RunNextCandleImportCommand(), cancellation.Token);

        await _jobs.Received().UpdateLeasedAsync(Arg.Is<CandleImportJob>(state => state!.ImportedCandles == 1), _worker.Id, CancellationToken.None);
    }

    [Test]
    public async Task Without_a_job_to_take_nothing_runs()
    {
        _jobs.ClaimNextAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((CandleImportJob?)null);

        bool found = await RunAsync();

        found.ShouldBeFalse();
        _marketHistory.ReceivedCalls().ShouldBeEmpty();
    }

    // Requirement "Аренда работы": the job is leased for the lease duration from now.
    [Test]
    public async Task Job_is_taken_under_a_lease_of_this_worker()
    {
        PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");

        bool found = await RunAsync();

        found.ShouldBeTrue();
        await _jobs.Received(1).ClaimNextAsync(_worker.Id, Now, Now + new WorkerSettings().LeaseDuration, Arg.Any<CancellationToken>());
    }

    // Scenario "Задача без продвижения".
    [Test]
    public async Task Job_taken_over_for_the_third_time_fails_without_loading()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z") with
        {
            Status = CandleImportStatus.Running,
            StartedAt = Now.AddHours(-1),
            LostLeases = 2
        };
        Claimable(job);

        await RunAsync();

        SavedStates().ShouldHaveSingleItem().ShouldBe(job.TakenOver(Now));
        _marketHistory.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Job_lost_before_it_starts_is_left_to_the_worker_that_took_it()
    {
        PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        _jobs.UpdateLeasedAsync(Arg.Any<CandleImportJob>(), _worker.Id, Arg.Any<CancellationToken>()).Returns(false);

        bool found = await RunAsync();

        found.ShouldBeTrue();
        _marketHistory.ReceivedCalls().ShouldBeEmpty();
    }

    // Scenario "Аренда перехвачена": once the page cannot be saved, the job stops and writes nothing more.
    [Test]
    public async Task Job_whose_lease_was_taken_over_stops_after_the_page_it_could_not_save()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(job, job.From, job.To, () => Candles("2024-01-01T02:00:00Z"), () => Candles("2024-01-01T01:00:00Z"));
        _jobs.UpdateLeasedAsync(Arg.Any<CandleImportJob>(), _worker.Id, Arg.Any<CancellationToken>()).Returns(true, false);

        bool found = await RunAsync();

        found.ShouldBeTrue();
        SavedStates().Count.ShouldBe(2);
        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z"]);
    }

    // Requirement "Аренда работы": a renewal that finds the lease taken over stops the job without another write.
    [Test]
    public async Task Job_stops_when_its_lease_cannot_be_renewed()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        _marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw Unavailable());
        _jobs.RenewLeaseAsync(job.Id, _worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        bool found = await RunAsync();

        found.ShouldBeTrue();
        SavedStates().ShouldHaveSingleItem().Status.ShouldBe(CandleImportStatus.Running);
    }

    [Test]
    public async Task Lease_is_renewed_while_the_job_waits_for_the_exchange()
    {
        CandleImportJob job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        var attempts = 0;
        _marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts < 4 ? throw Unavailable() : Pages([() => Candles("2024-01-01T02:00:00Z")]));

        await RunAsync();

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Succeeded);
        await _jobs.Received().RenewLeaseAsync(job.Id, _worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private CandleImportJob PendingJob(string from, string to, int lostLeases = 0)
    {
        var job = new CandleImportJob(
            Guid.NewGuid(),
            InstrumentGenerator.Generate(),
            Interval.OneHour,
            At(from),
            At(to),
            CandleImportStatus.Pending,
            ImportedCandles: 0,
            Error: null,
            Now,
            StartedAt: null,
            FinishedAt: null,
            lostLeases);
        Claimable(job);

        return job;
    }

    /// <summary>The next claim leases the job as stored, later ones find nothing.</summary>
    private void Claimable(CandleImportJob job)
    {
        _jobs.ClaimNextAsync(_worker.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(job, (CandleImportJob?)null);
    }

    /// <summary>Runs the next job, moving fake time forward while it waits between attempts.</summary>
    private async Task<bool> RunAsync()
    {
        Task<bool> run = _handler.Handle(new RunNextCandleImportCommand(), CancellationToken.None);

        while (!run.IsCompleted)
        {
            await Task.Delay(5);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        return await run;
    }

    private void Stored(CandleImportJob job, params string[] openTimes)
    {
        _candles.GetOpenTimesAsync(job.Instrument, job.Interval, job.From, job.To, Arg.Any<CancellationToken>())
            .Returns(openTimes.Select(At).ToList());
    }

    /// <summary>The exchange serves the pages of a range in order; a page may run code first, e.g. throw to simulate an exchange error.</summary>
    private void ServePages(CandleImportJob job, DateTimeOffset from, DateTimeOffset to, params Func<IReadOnlyList<Candle>>[] pages)
    {
        _marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, from, to, Arg.Any<CancellationToken>()).Returns(_ => Pages(pages));
    }

    /// <summary>Each request of the job, whatever its range, is served the pages of the next attempt; its time is recorded.</summary>
    private void ServeAttempts(CandleImportJob job, List<DateTimeOffset> requestTimes, params Func<IReadOnlyList<Candle>>[][] attempts)
    {
        var attempt = 0;
        _marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requestTimes.Add(_time.GetUtcNow());

                return Pages(attempts[attempt++]);
            });
    }

    private static async IAsyncEnumerable<IReadOnlyList<Candle>> Pages(Func<IReadOnlyList<Candle>>[] pages)
    {
        foreach (Func<IReadOnlyList<Candle>> page in pages)
        {
            await Task.Yield();

            yield return page();
        }
    }

    private static ExchangeUnavailableException Unavailable()
    {
        return new ExchangeUnavailableException("Bybit request failed after 5 retries: HTTP 503");
    }

    private static IReadOnlyList<Candle> Candles(params string[] openTimes)
    {
        return openTimes.Select(time => CandleGenerator.Generate(At(time))).ToList();
    }

    private static DateTimeOffset At(string time)
    {
        return DateTimeOffset.Parse(time);
    }

    /// <summary>Every state of the job the handler saved, in order.</summary>
    private List<CandleImportJob> SavedStates()
    {
        return _jobs.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ICandleImportJobRepository.UpdateLeasedAsync))
            .Select(call => (CandleImportJob)call.GetArguments()[0]!)
            .ToList();
    }

    private List<string> StoredOpenTimes()
    {
        return _candles.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ICandleRepository.UpsertAsync))
            .SelectMany(call => (IReadOnlyCollection<Candle>)call.GetArguments()[2]!)
            .Select(candle => Format(candle.OpenTime))
            .ToList();
    }

    private List<(string From, string To)> RequestedRanges()
    {
        return _marketHistory.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMarketHistoryClient.GetCandlePagesAsync))
            .Select(call => (Format((DateTimeOffset)call.GetArguments()[2]!), Format((DateTimeOffset)call.GetArguments()[3]!)))
            .ToList();
    }

    private static string Format(DateTimeOffset time)
    {
        return time.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}