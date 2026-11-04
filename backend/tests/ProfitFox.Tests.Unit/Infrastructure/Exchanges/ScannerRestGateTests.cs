using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Scanner;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Repositories;
using ProfitFox.Infrastructure.Exchanges;
using ProfitFox.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges;

/// <summary>Requirement "Лимиты Bybit": every REST request of the scanner goes through the gate.</summary>
public class ScannerRestGateTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private FakeTimeProvider _time = null!;
    private IScannerStateRepository _state = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _state = Substitute.For<IScannerStateRepository>();
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(null, 0, 0, null));
    }

    [Test]
    public async Task Requests_go_no_faster_than_the_setting()
    {
        using ScannerRestGate gate = Gate(new ScannerSettings { RestRequestsPerSecond = 2 });
        var sent = 0;

        Task<int>[] requests = [.. Enumerable.Range(0, 5).Select(_ => gate.SendAsync(_ => ValueTask.FromResult(Interlocked.Increment(ref sent)), CancellationToken.None))];
        await Task.Delay(50);
        int first = sent;
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        int second = sent;
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(requests);

        first.ShouldBe(2);
        second.ShouldBe(4);
        sent.ShouldBe(5);
    }

    // Scenario "Bybit ответил 403".
    [Test]
    public async Task No_request_goes_until_the_pause_after_a_rate_limit_ends()
    {
        using ScannerRestGate gate = Gate(new ScannerSettings { RestPause = TimeSpan.FromMinutes(10) });
        var sent = 0;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw new TransientExchangeException("HTTP 403", rateLimited: true), CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(1));
        int after = await gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None);

        after.ShouldBe(1);
        await _state.Received(1).PauseRestAsync(Start.AddMinutes(10), Arg.Any<CancellationToken>());
    }

    // A pause recorded by the scanner before a restart, or by the holder another scanner took over from, still holds.
    [Test]
    public async Task Pause_recorded_before_the_start_of_the_process_holds()
    {
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(new ScannerState(null, 0, 0, Start.AddMinutes(5)));
        using ScannerRestGate gate = Gate(new ScannerSettings());
        var sent = 0;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(5));
        int after = await gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None);

        after.ShouldBe(1);
    }

    [Test]
    public async Task Pause_is_read_again_when_the_first_read_failed()
    {
        _state.GetAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<ScannerState>(new InvalidOperationException("database down")),
            Task.FromResult(new ScannerState(null, 0, 0, null)));
        using ScannerRestGate gate = Gate(new ScannerSettings());

        await Should.ThrowAsync<InvalidOperationException>(() => gate.SendAsync(_ => ValueTask.FromResult(1), CancellationToken.None));

        (await gate.SendAsync(_ => ValueTask.FromResult(2), CancellationToken.None)).ShouldBe(2);
    }

    [Test]
    public async Task Other_transient_failure_is_retried_after_a_second()
    {
        using ScannerRestGate gate = Gate(new ScannerSettings());
        var times = new List<DateTimeOffset>();

        Task<string> request = gate.SendAsync(
            _ =>
            {
                times.Add(_time.GetUtcNow());

                return times.Count == 1 ? throw new TransientExchangeException("HTTP 502") : ValueTask.FromResult("ok");
            },
            CancellationToken.None);

        while (!request.IsCompleted)
        {
            await Task.Delay(20);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        (await request).ShouldBe("ok");
        times.ShouldBe([Start, Start.AddSeconds(1)]);
    }

    private ScannerRestGate Gate(ScannerSettings settings)
    {
        return new ScannerRestGate(settings, new ExchangeResilienceExecutor(_time), _state, _time);
    }
}