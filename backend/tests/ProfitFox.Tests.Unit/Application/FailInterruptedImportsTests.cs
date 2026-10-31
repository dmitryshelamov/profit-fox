using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.MarketData.Commands.FailInterruptedImports;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Tests.Unit.Application;

public class FailInterruptedImportsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    // Scenario "Перезапуск во время загрузки": choosing Pending and Running jobs is the repository's part, see its integration tests.
    [Test]
    public async Task Unfinished_jobs_fail_as_interrupted_by_restart()
    {
        ICandleImportJobRepository jobs = Substitute.For<ICandleImportJobRepository>();
        var handler = new FailInterruptedImportsCommandHandler(jobs, new FakeTimeProvider(Now));

        await handler.Handle(new FailInterruptedImportsCommand(), CancellationToken.None);

        await jobs.Received(1).FailUnfinishedAsync(FailInterruptedImportsCommandHandler.InterruptedByRestart, Now, Arg.Any<CancellationToken>());
    }
}