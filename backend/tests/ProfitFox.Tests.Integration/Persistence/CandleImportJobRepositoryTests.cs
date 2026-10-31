using Npgsql;

using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class CandleImportJobRepositoryTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private CandleImportJobRepository _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _store = new CandleImportJobRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Created_job_is_read_back_with_all_parameters()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);

        await _store.AddAsync(job, CancellationToken.None);

        (await _store.GetAsync(job.Id, CancellationToken.None)).ShouldBe(job);
    }

    [Test]
    public async Task Unknown_job_is_not_found()
    {
        (await _store.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Update_saves_status_progress_error_and_times()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        await _store.AddAsync(job, CancellationToken.None);
        CandleImportJob failed = job with
        {
            Status = CandleImportStatus.Failed,
            ImportedCandles = 1500,
            Error = "params error",
            StartedAt = Created.AddMinutes(1),
            FinishedAt = Created.AddMinutes(2)
        };

        await _store.UpdateAsync(failed, CancellationToken.None);

        (await _store.GetAsync(job.Id, CancellationToken.None)).ShouldBe(failed);
    }

    [Test]
    public async Task Unfinished_jobs_become_failed_with_the_reason()
    {
        CandleImportJob pending = NewJob(CandleImportStatus.Pending);
        CandleImportJob running = NewJob(CandleImportStatus.Running) with { StartedAt = Created };
        CandleImportJob succeeded = NewJob(CandleImportStatus.Succeeded) with { StartedAt = Created, FinishedAt = Created, ImportedCandles = 3 };

        foreach (CandleImportJob? job in new[] { pending, running, succeeded })
        {
            await _store.AddAsync(job, CancellationToken.None);
        }

        DateTimeOffset now = Created.AddHours(1);
        await _store.FailUnfinishedAsync("interrupted", now, CancellationToken.None);

        (await _store.GetAsync(pending.Id, CancellationToken.None)).ShouldBe(
            pending with { Status = CandleImportStatus.Failed, Error = "interrupted", FinishedAt = now });
        (await _store.GetAsync(running.Id, CancellationToken.None)).ShouldBe(
            running with { Status = CandleImportStatus.Failed, Error = "interrupted", FinishedAt = now });
        (await _store.GetAsync(succeeded.Id, CancellationToken.None)).ShouldBe(succeeded);
    }

    private static CandleImportJob NewJob(CandleImportStatus status)
    {
        return CandleImportJobGenerator.Generate(status);
    }
}