using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.MarketData;

/// <summary>Requirement "Прерванные задачи после сбоя Worker'а".</summary>
public class CandleImportJobTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:30:00Z");

    [Test]
    public void Pending_job_taken_by_a_worker_starts_running_without_a_lost_lease()
    {
        CandleImportJob started = CandleImportJobGenerator.Generate(CandleImportStatus.Pending).TakenOver(Now);

        (started.Status, started.StartedAt, started.LostLeases).ShouldBe((CandleImportStatus.Running, Now, 0));
    }

    // Scenarios "Worker упал во время загрузки" and "Задача без продвижения".
    [TestCase(0, CandleImportStatus.Running, 1)]
    [TestCase(1, CandleImportStatus.Running, 2)]
    [TestCase(2, CandleImportStatus.Failed, 3)]
    public void Running_job_taken_over_counts_one_more_lost_lease_and_the_third_one_fails_it(int lost, CandleImportStatus status, int counted)
    {
        DateTimeOffset started = Now.AddHours(-1);
        CandleImportJob running = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with { StartedAt = started, LostLeases = lost };

        CandleImportJob taken = running.TakenOver(Now);

        (taken.Status, taken.LostLeases, taken.StartedAt).ShouldBe((status, counted, started));
    }

    [Test]
    public void Job_failed_by_its_third_lost_lease_says_why()
    {
        CandleImportJob running = CandleImportJobGenerator.Generate(CandleImportStatus.Running) with { LostLeases = 2 };

        CandleImportJob failed = running.TakenOver(Now);

        (failed.Error, failed.FinishedAt).ShouldBe((CandleImportJob.InterruptedWithoutProgress, Now));
    }
}