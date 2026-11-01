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
    public async Task Update_saves_status_progress_error_times_and_lost_leases()
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
            LostLeases = 2
        };

        await _store.UpdateAsync(failed, CancellationToken.None);

        (await _store.GetAsync(job.Id, CancellationToken.None)).ShouldBe(failed);
    }

    // Requirement "Аренда работы".
    [Test]
    public async Task Oldest_pending_job_is_leased_to_the_worker_that_claims_it()
    {
        CandleImportJob newer = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created.AddMinutes(1) };
        CandleImportJob older = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created };
        CandleImportJob succeeded = NewJob(CandleImportStatus.Succeeded) with { CreatedAt = Created.AddMinutes(-1), FinishedAt = Created };
        await AddAsync(newer, older, succeeded);

        CandleImportJob? claimed = await _store.ClaimNextAsync(Guid.NewGuid(), Created, Created.AddSeconds(30), CancellationToken.None);

        claimed.ShouldBe(older);
    }

    // Scenario "Две задачи и два Worker'а".
    [Test]
    public async Task No_worker_takes_a_job_while_another_job_holds_a_live_lease()
    {
        CandleImportJob first = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created };
        CandleImportJob second = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created.AddMinutes(1) };
        await AddAsync(first, second);
        var a = Guid.NewGuid();
        await _store.ClaimNextAsync(a, Created, Created.AddSeconds(30), CancellationToken.None);

        CandleImportJob? whileRunning = await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(10), Created.AddSeconds(40), CancellationToken.None);
        await _store.UpdateLeasedAsync(first with { Status = CandleImportStatus.Succeeded, FinishedAt = Created.AddSeconds(20) }, a, CancellationToken.None);
        CandleImportJob? afterwards = await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(21), Created.AddSeconds(51), CancellationToken.None);

        whileRunning.ShouldBeNull();
        afterwards.ShouldBe(second);
    }

    [Test]
    public async Task Workers_claiming_at_once_take_one_job_between_them()
    {
        await AddAsync(NewJob(CandleImportStatus.Pending) with { CreatedAt = Created }, NewJob(CandleImportStatus.Pending) with { CreatedAt = Created.AddMinutes(1) });

        CandleImportJob?[] claimed = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => _store.ClaimNextAsync(Guid.NewGuid(), Created, Created.AddSeconds(30), CancellationToken.None)));

        claimed.Count(job => job is not null).ShouldBe(1);
    }

    // Scenario "Порядок после сбоя".
    [Test]
    public async Task Job_created_earlier_goes_before_a_later_one_whose_lease_expired()
    {
        CandleImportJob older = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created };
        CandleImportJob newer = NewJob(CandleImportStatus.Pending) with { CreatedAt = Created.AddMinutes(1) };
        await AddAsync(newer);
        await _store.ClaimNextAsync(Guid.NewGuid(), Created, Created.AddSeconds(30), CancellationToken.None);
        await AddAsync(older);

        CandleImportJob? first = await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(31), Created.AddSeconds(61), CancellationToken.None);

        first.ShouldBe(older);
    }

    // Scenario "Worker упал во время загрузки".
    [Test]
    public async Task Running_job_whose_lease_expired_is_taken_by_another_worker()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        await AddAsync(job);
        var a = Guid.NewGuid();
        await _store.ClaimNextAsync(a, Created, Created.AddSeconds(30), CancellationToken.None);
        CandleImportJob running = job with { Status = CandleImportStatus.Running, StartedAt = Created };
        await _store.UpdateLeasedAsync(running, a, CancellationToken.None);

        CandleImportJob? taken = await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(30), Created.AddSeconds(60), CancellationToken.None);

        taken.ShouldBe(running);
    }

    // Scenario "Аренда перехвачена".
    [Test]
    public async Task Worker_whose_lease_was_taken_over_writes_nothing_and_cannot_renew()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        await AddAsync(job);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await _store.ClaimNextAsync(a, Created, Created.AddSeconds(30), CancellationToken.None);
        await _store.ClaimNextAsync(b, Created.AddSeconds(31), Created.AddSeconds(61), CancellationToken.None);

        bool written = await _store.UpdateLeasedAsync(job with { ImportedCandles = 5 }, a, CancellationToken.None);
        bool renewed = await _store.RenewLeaseAsync(job.Id, a, Created.AddSeconds(90), CancellationToken.None);

        (written, renewed).ShouldBe((false, false));
        (await _store.GetAsync(job.Id, CancellationToken.None))!.ImportedCandles.ShouldBe(0);
        (await _store.UpdateLeasedAsync(job with { ImportedCandles = 5 }, b, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task Renewed_lease_keeps_the_job_from_other_workers()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        await AddAsync(job);
        var a = Guid.NewGuid();
        await _store.ClaimNextAsync(a, Created, Created.AddSeconds(30), CancellationToken.None);

        (await _store.RenewLeaseAsync(job.Id, a, Created.AddSeconds(60), CancellationToken.None)).ShouldBeTrue();

        (await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(31), Created.AddSeconds(61), CancellationToken.None)).ShouldBeNull();
    }

    // A retried job must be free for any Worker at once, and a finished one never blocks the next job.
    [Test]
    public async Task Finished_job_ends_its_lease()
    {
        CandleImportJob job = NewJob(CandleImportStatus.Pending);
        CandleImportJob next = NewJob(CandleImportStatus.Pending) with { CreatedAt = job.CreatedAt.AddMinutes(1) };
        await AddAsync(job, next);
        var a = Guid.NewGuid();
        await _store.ClaimNextAsync(a, Created, Created.AddSeconds(30), CancellationToken.None);
        await _store.UpdateLeasedAsync(job with { Status = CandleImportStatus.Failed, Error = "params error", FinishedAt = Created }, a, CancellationToken.None);
        await _store.UpdateAsync(job with { Status = CandleImportStatus.Pending }, CancellationToken.None);

        (await _store.ClaimNextAsync(Guid.NewGuid(), Created.AddSeconds(1), Created.AddSeconds(31), CancellationToken.None))!.Id.ShouldBe(job.Id);
    }

    private async Task AddAsync(params CandleImportJob[] jobs)
    {
        foreach (CandleImportJob job in jobs)
        {
            await _store.AddAsync(job, CancellationToken.None);
        }
    }

    private static CandleImportJob NewJob(CandleImportStatus status)
    {
        return CandleImportJobGenerator.Generate(status);
    }
}