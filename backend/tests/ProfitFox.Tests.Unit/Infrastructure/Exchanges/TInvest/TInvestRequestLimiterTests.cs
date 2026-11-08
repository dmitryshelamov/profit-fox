using Microsoft.Extensions.Time.Testing;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: a sliding window of a minute per token and group, and a pause of a group until the reset the server named.</summary>
public class TInvestRequestLimiterTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private FakeTimeProvider _time = null!;
    private TInvestRequestLimiter _limiter = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _limiter = new TInvestRequestLimiter(new TInvestSettings { Limits = new Dictionary<string, int> { ["MarketData"] = 2, ["Instruments"] = 1 } }, _time);
    }

    [Test]
    public async Task Requests_within_the_limit_go_at_once()
    {
        await _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);
        Task second = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);

        second.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Test]
    public async Task Request_over_the_limit_waits_until_the_window_lets_it_through()
    {
        await _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(10));
        await _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);

        Task third = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);

        third.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(50) - TimeSpan.FromTicks(1));
        third.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromTicks(1));
        await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Groups_have_windows_of_their_own()
    {
        await _limiter.AcquireAsync("t.one", TInvestMethodGroup.Instruments, CancellationToken.None);

        Task marketData = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);
        Task instruments = _limiter.AcquireAsync("t.one", TInvestMethodGroup.Instruments, CancellationToken.None);

        marketData.IsCompletedSuccessfully.ShouldBeTrue();
        instruments.IsCompleted.ShouldBeFalse();
    }

    [Test]
    public async Task Tokens_do_not_share_a_window()
    {
        await _limiter.AcquireAsync("t.one", TInvestMethodGroup.Instruments, CancellationToken.None);

        Task other = _limiter.AcquireAsync("t.two", TInvestMethodGroup.Instruments, CancellationToken.None);

        other.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Test]
    public async Task Pause_holds_the_group_of_the_token_until_the_reset()
    {
        _limiter.Pause("t.one", TInvestMethodGroup.MarketData, Start + TimeSpan.FromSeconds(7));

        Task held = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);
        Task otherGroup = _limiter.AcquireAsync("t.one", TInvestMethodGroup.Instruments, CancellationToken.None);

        held.IsCompleted.ShouldBeFalse();
        otherGroup.IsCompletedSuccessfully.ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(7));
        await held.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void Earlier_pause_does_not_shorten_a_later_one()
    {
        _limiter.Pause("t.one", TInvestMethodGroup.MarketData, Start + TimeSpan.FromSeconds(30));
        _limiter.Pause("t.one", TInvestMethodGroup.MarketData, Start + TimeSpan.FromSeconds(7));

        Task held = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(7));

        held.IsCompleted.ShouldBeFalse();
    }

    [Test]
    public async Task Group_without_a_limit_in_the_settings_goes_at_once()
    {
        Task users = _limiter.AcquireAsync("t.one", TInvestMethodGroup.Users, CancellationToken.None);

        users.IsCompletedSuccessfully.ShouldBeTrue();
        await users;
    }

    [Test]
    public async Task Waiting_request_is_cancelled_with_its_token()
    {
        _limiter.Pause("t.one", TInvestMethodGroup.MarketData, Start + TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource();

        Task held = _limiter.AcquireAsync("t.one", TInvestMethodGroup.MarketData, cancel.Token);
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => held);
    }
}