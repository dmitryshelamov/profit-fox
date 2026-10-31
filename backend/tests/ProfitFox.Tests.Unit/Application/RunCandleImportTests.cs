using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.MarketData.Commands.RunCandleImport;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.MarketData;
using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

public class RunCandleImportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private IMarketHistoryClient _marketHistory = null!;
    private ICandleImportJobRepository _jobs = null!;
    private ICandleRepository _candles = null!;
    private RunCandleImportCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _marketHistory = Substitute.For<IMarketHistoryClient>();
        _jobs = Substitute.For<ICandleImportJobRepository>();
        _candles = Substitute.For<ICandleRepository>();
        _handler = new RunCandleImportCommandHandler(_marketHistory, _jobs, _candles, new FakeTimeProvider(Now));
    }

    [Test]
    public async Task Job_is_running_from_the_start_of_execution()
    {
        var job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(job);

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        SavedStates()[0].ShouldBe(job with { Status = CandleImportStatus.Running, StartedAt = Now });
    }

    // Scenarios "Успешное завершение" and "Границы периода".
    [Test]
    public async Task Stores_candles_of_the_period_and_succeeds()
    {
        var job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(
            job,
            () => Candles("2024-01-01T03:00:00Z", "2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z"),
            () => Candles("2024-01-01T00:00:00Z", "2023-12-31T23:00:00Z"));

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z", "2024-01-01T00:00:00Z"]);
        SavedStates()[^1].ShouldBe(job with { Status = CandleImportStatus.Succeeded, ImportedCandles = 3, StartedAt = Now, FinishedAt = Now });
    }

    // Scenario "Период включает текущий момент".
    [Test]
    public async Task Drops_the_candle_that_is_not_closed_yet()
    {
        var job = PendingJob("2024-03-10T10:00:00Z", "2024-03-10T12:30:00Z");
        ServePages(job, () => Candles("2024-03-10T12:00:00Z", "2024-03-10T11:00:00Z", "2024-03-10T10:00:00Z"));

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        StoredOpenTimes().ShouldBe(["2024-03-10T11:00:00Z", "2024-03-10T10:00:00Z"]);
    }

    [Test]
    public async Task Progress_is_saved_after_each_page()
    {
        var job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        long importedBeforeSecondPage = -1;
        ServePages(
            job,
            () => Candles("2024-01-01T02:00:00Z", "2024-01-01T01:00:00Z"),
            () =>
            {
                importedBeforeSecondPage = SavedStates()[^1].ImportedCandles;
                return Candles("2024-01-01T00:00:00Z");
            });

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        importedBeforeSecondPage.ShouldBe(2);
    }

    // Scenario "Нет данных за весь период".
    [Test]
    public async Task Period_before_listing_succeeds_with_no_candles()
    {
        var job = PendingJob("2010-01-01T00:00:00Z", "2010-02-01T00:00:00Z");
        ServePages(job);

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        SavedStates()[^1].Status.ShouldBe(CandleImportStatus.Succeeded);
        SavedStates()[^1].ImportedCandles.ShouldBe(0);
    }

    // Scenario "Ошибка биржи".
    [Test]
    public async Task Bybit_error_fails_the_job_and_keeps_saved_candles()
    {
        var job = PendingJob("2024-01-01T00:00:00Z", "2024-01-01T03:00:00Z");
        ServePages(
            job,
            () => Candles("2024-01-01T02:00:00Z"),
            () => throw new ExchangeException("Bybit request failed after 5 retries: HTTP 503"));

        await _handler.Handle(new RunCandleImportCommand(job.Id), CancellationToken.None);

        SavedStates()[^1].ShouldBe(job with
        {
            Status = CandleImportStatus.Failed,
            ImportedCandles = 1,
            Error = "Bybit request failed after 5 retries: HTTP 503",
            StartedAt = Now,
            FinishedAt = Now,
        });
        StoredOpenTimes().ShouldBe(["2024-01-01T02:00:00Z"]);
    }

    private CandleImportJob PendingJob(string from, string to)
    {
        var job = new CandleImportJob(
            Guid.NewGuid(), InstrumentGenerator.Generate(), Interval.OneHour, DateTimeOffset.Parse(from), DateTimeOffset.Parse(to),
            CandleImportStatus.Pending, 0, null, Now, null, null);
        _jobs.GetAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        return job;
    }

    /// <summary>Bybit serves the pages in order; a page may run code first, e.g. throw to simulate a Bybit error.</summary>
    private void ServePages(CandleImportJob job, params Func<IReadOnlyList<Candle>>[] pages) =>
        _marketHistory.GetCandlePagesAsync(job.Instrument, job.Interval, job.From, job.To, Arg.Any<CancellationToken>()).Returns(Pages(pages));

    private static async IAsyncEnumerable<IReadOnlyList<Candle>> Pages(Func<IReadOnlyList<Candle>>[] pages)
    {
        foreach (var page in pages)
        {
            await Task.Yield();
            yield return page();
        }
    }

    private static IReadOnlyList<Candle> Candles(params string[] openTimes) =>
        openTimes.Select(time => CandleGenerator.Generate(DateTimeOffset.Parse(time))).ToList();

    /// <summary>Every state of the job the handler saved, in order.</summary>
    private List<CandleImportJob> SavedStates() =>
        _jobs.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ICandleImportJobRepository.UpdateAsync))
            .Select(call => (CandleImportJob)call.GetArguments()[0]!)
            .ToList();

    private List<string> StoredOpenTimes() =>
        _candles.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ICandleRepository.UpsertAsync))
            .SelectMany(call => (IReadOnlyCollection<Candle>)call.GetArguments()[2]!)
            .Select(candle => candle.OpenTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            .ToList();
}
