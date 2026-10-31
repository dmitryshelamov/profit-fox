using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.MarketData.Commands.RequeueInterruptedImports;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application;

// Choosing Pending and Running jobs and their order is the repository's part, see its integration tests.
public class RequeueInterruptedImportsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    private ICandleImportJobRepository _jobs = null!;
    private ICandleImportQueue _queue = null!;
    private RequeueInterruptedImportsCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _jobs = Substitute.For<ICandleImportJobRepository>();
        _queue = Substitute.For<ICandleImportQueue>();
        _handler = new RequeueInterruptedImportsCommandHandler(_jobs, _queue, new FakeTimeProvider(Now));
    }

    // Scenario "Перезапуск во время загрузки".
    [Test]
    public async Task Running_job_is_queued_again_as_pending_with_one_more_stalled_restart()
    {
        CandleImportJob running = Running(stalledRestarts: 1);
        Unfinished(running);

        await RequeueAsync();

        await _jobs.Received(1).UpdateAsync(running with { Status = CandleImportStatus.Pending, StalledRestarts = 2 }, Arg.Any<CancellationToken>());
        QueuedJobs().ShouldBe([running.Id]);
    }

    // Scenario "Задача в очереди при перезапуске".
    [Test]
    public async Task Pending_job_is_queued_unchanged()
    {
        CandleImportJob pending = CandleImportJobGenerator.Generate(CandleImportStatus.Pending);
        Unfinished(pending);

        await RequeueAsync();

        await _jobs.DidNotReceive().UpdateAsync(Arg.Any<CandleImportJob>(), Arg.Any<CancellationToken>());
        QueuedJobs().ShouldBe([pending.Id]);
    }

    // Scenario "Порядок возобновления".
    [Test]
    public async Task Jobs_are_queued_in_the_order_they_were_read()
    {
        CandleImportJob olderPending = CandleImportJobGenerator.Generate(CandleImportStatus.Pending);
        CandleImportJob newerRunning = Running(stalledRestarts: 0);
        Unfinished(olderPending, newerRunning);

        await RequeueAsync();

        QueuedJobs().ShouldBe([olderPending.Id, newerRunning.Id]);
    }

    // Scenario "Задача без продвижения".
    [Test]
    public async Task Third_stalled_restart_fails_the_job()
    {
        CandleImportJob running = Running(stalledRestarts: 2);
        Unfinished(running);

        await RequeueAsync();

        await _jobs.Received(1)
            .UpdateAsync(
                running with
                {
                    Status = CandleImportStatus.Failed,
                    Error = RequeueInterruptedImportsCommandHandler.InterruptedByRestart,
                    FinishedAt = Now,
                    StalledRestarts = 3
                },
                Arg.Any<CancellationToken>());
        QueuedJobs().ShouldBeEmpty();
    }

    private static CandleImportJob Running(int stalledRestarts)
    {
        return CandleImportJobGenerator.Generate(CandleImportStatus.Running) with
        {
            StartedAt = Now.AddHours(-1),
            ImportedCandles = 10,
            StalledRestarts = stalledRestarts
        };
    }

    private void Unfinished(params CandleImportJob[] jobs)
    {
        _jobs.GetUnfinishedAsync(Arg.Any<CancellationToken>()).Returns(jobs);
    }

    private Task RequeueAsync()
    {
        return _handler.Handle(new RequeueInterruptedImportsCommand(), CancellationToken.None);
    }

    private List<Guid> QueuedJobs()
    {
        return _queue.ReceivedCalls().Select(call => (Guid)call.GetArguments()[0]!).ToList();
    }
}