using Npgsql;
using ProfitFox.Application.MarketData.Commands.FailInterruptedImports;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;
using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

public class RestartTests
{
    // Scenario "Перезапуск во время загрузки".
    [Test]
    public async Task Job_running_when_the_service_stopped_is_failed_after_start()
    {
        var connectionString = await IntegrationEnvironment.CreateDatabaseAsync();
        var running = await StartAndLeaveRunningJobAsync(connectionString);

        await using var restarted = new ProfitFoxFactory(connectionString);
        restarted.CreateClient().Dispose();

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var job = await new CandleImportJobRepository(dataSource).GetAsync(running.Id, CancellationToken.None);
        job!.Status.ShouldBe(CandleImportStatus.Failed);
        job.Error.ShouldBe(FailInterruptedImportsCommandHandler.InterruptedByRestart);
        job.FinishedAt.ShouldBe(restarted.Time.GetUtcNow());
    }

    private static async Task<CandleImportJob> StartAndLeaveRunningJobAsync(string connectionString)
    {
        await using var factory = new ProfitFoxFactory(connectionString);
        factory.CreateClient().Dispose();
        var job = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with { StartedAt = factory.Time.GetUtcNow() };
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new CandleImportJobRepository(dataSource).AddAsync(job, CancellationToken.None);
        return job;
    }
}
