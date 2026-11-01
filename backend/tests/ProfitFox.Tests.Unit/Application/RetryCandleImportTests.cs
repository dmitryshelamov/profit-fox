using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.MarketData.Commands.RetryCandleImport;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

public class RetryCandleImportTests
{
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ICandleImportJobRepository _jobs = null!;
    private RetryCandleImportCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _jobs = Substitute.For<ICandleImportJobRepository>();
        _handler = new RetryCandleImportCommandHandler(_jobs);
    }

    // Scenarios "Повтор упавшей задачи" and "Ручной повтор сбрасывает отсчёт прерываний".
    [Test]
    public async Task Failed_job_is_pending_again()
    {
        CandleImportJob failed = Stored(
            CandleImportJobGenerator.Generate(CandleImportStatus.Failed) with
            {
                ImportedCandles = 10,
                Error = "Bybit request failed after 5 retries: HTTP 503",
                StartedAt = Started,
                FinishedAt = Started.AddMinutes(5),
                LostLeases = 2
            });

        await RetryAsync(failed.Id);

        await _jobs.Received(1)
            .UpdateAsync(
                failed with { Status = CandleImportStatus.Pending, Error = null, FinishedAt = null, LostLeases = 0 },
                Arg.Any<CancellationToken>());
    }

    // Scenario "Повтор незавершённой или успешной задачи".
    [TestCase(CandleImportStatus.Pending)]
    [TestCase(CandleImportStatus.Running)]
    [TestCase(CandleImportStatus.Succeeded)]
    public async Task Job_that_has_not_failed_is_a_conflict_and_stays_as_it_is(CandleImportStatus status)
    {
        CandleImportJob job = Stored(CandleImportJobGenerator.Generate(status));

        await Should.ThrowAsync<ConflictException>(() => RetryAsync(job.Id));

        await _jobs.DidNotReceive().UpdateAsync(Arg.Any<CandleImportJob>(), Arg.Any<CancellationToken>());
    }

    // Scenario "Повтор неизвестной задачи".
    [Test]
    public async Task Unknown_job_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(() => RetryAsync(Guid.NewGuid()));
    }

    private CandleImportJob Stored(CandleImportJob job)
    {
        _jobs.GetAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        return job;
    }

    private Task RetryAsync(Guid id)
    {
        return _handler.Handle(new RetryCandleImportCommand(id), CancellationToken.None);
    }
}