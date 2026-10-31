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
    public async Task Update_saves_status_progress_error_times_and_interruptions()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        await _store.AddAsync(job, CancellationToken.None);
        CandleImportJob failed = job with
        {
            Status = CandleImportStatus.Failed,
            ImportedCandles = 1500,
            Error = "params error",
            StartedAt = Created.AddMinutes(1),
            FinishedAt = Created.AddMinutes(2),
            InterruptionsWithoutProgress = 2
        };

        await _store.UpdateAsync(failed, CancellationToken.None);

        (await _store.GetAsync(job.Id, CancellationToken.None)).ShouldBe(failed);
    }

    [Test]
    public async Task Unfinished_jobs_are_read_oldest_first()
    {
        CandleImportJob newerRunning = NewJob(CandleImportStatus.Running) with { CreatedAt = Created.AddMinutes(2), StartedAt = Created.AddMinutes(3) };
        CandleImportJob olderPending = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created };
        CandleImportJob middlePending = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created.AddMinutes(1) };
        CandleImportJob succeeded = NewJob(CandleImportStatus.Succeeded) with { CreatedAt = Created, StartedAt = Created, FinishedAt = Created };
        CandleImportJob failed = NewJob(CandleImportStatus.Failed) with { CreatedAt = Created, Error = "params error", FinishedAt = Created };

        foreach (CandleImportJob job in new[] { newerRunning, olderPending, succeeded, middlePending, failed })
        {
            await _store.AddAsync(job, CancellationToken.None);
        }

        (await _store.GetUnfinishedAsync(CancellationToken.None)).ShouldBe([olderPending, middlePending, newerRunning]);
    }

    private static CandleImportJob NewJob(CandleImportStatus status)
    {
        return CandleImportJobGenerator.Generate(status);
    }
}