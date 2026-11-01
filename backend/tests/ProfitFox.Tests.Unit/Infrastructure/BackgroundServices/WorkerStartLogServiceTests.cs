using Microsoft.Extensions.Logging;

using NSubstitute;

using ProfitFox.Application.Workers;
using ProfitFox.Infrastructure.BackgroundServices;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>The lease owner in the database is matched to a machine by the log of the Worker's start.</summary>
public class WorkerStartLogServiceTests
{
    [Test]
    public async Task Start_of_a_worker_logs_its_lease_owner_and_machine()
    {
        var worker = new WorkerIdentity(Guid.NewGuid());
        ILogger log = Substitute.For<ILogger>();
        log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(log);
        var service = new WorkerStartLogService(worker, new Logger<WorkerStartLogService>(loggers));

        await service.StartAsync(CancellationToken.None);

        object?[] logged = log.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)).ShouldHaveSingleItem().GetArguments();
        ((LogLevel)logged[0]!).ShouldBe(LogLevel.Information);
        var state = (IReadOnlyList<KeyValuePair<string, object?>>)logged[2]!;
        state.ShouldContain(new KeyValuePair<string, object?>("WorkerId", worker.Id));
        state.ShouldContain(new KeyValuePair<string, object?>("Machine", Environment.MachineName));
    }
}