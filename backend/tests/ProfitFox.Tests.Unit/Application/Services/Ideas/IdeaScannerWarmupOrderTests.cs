using ProfitFox.Application.Ports.MarketData.Feed;
using ProfitFox.Application.Services.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;

using static ProfitFox.Tests.Unit.Application.Services.Ideas.ScannerRig;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     The warm-up pair after pair (spec strategy-ideas «Состояние сканера в разделе «Идеи»»): every run of a pair is read
///     before the next pair, the runs of a pair on one timeframe share one read of its history, and the warm-up tells how
///     long it has still to go. On prod 14 strategies on four timeframes read the same candles 14 times over, and the count
///     of warm pairs stood at 0 until the last run reached the pairs.
/// </summary>
public sealed class IdeaScannerWarmupOrderTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public async Task The_strategies_of_a_timeframe_share_one_read_of_a_pair_and_the_pairs_go_one_by_one()
    {
        var rig = new ScannerRig(settings: new ScannerSettings { WarmupParallelism = 1 });
        await rig.Enable("close-up", "8h", timeframes: [Hour, FourHours]);
        await rig.Enable("close-up-too", "8h", timeframes: [Hour, FourHours]);

        await rig.Start();

        Assert.That(rig.History.Asked, Is.EqualTo(new[]
        {
            "candles btc", "candles btc", "candles eth", "candles eth", "candles sol", "candles sol"
        }), "one read of a pair on each timeframe, pair after pair");
        Assert.That(rig.Scanner.Progress, Is.EqualTo((3, 3)), "both strategies are warm on every pair");
    }

    [Test]
    public async Task The_warm_up_tells_how_long_it_has_still_to_go_at_its_speed()
    {
        var rig = new ScannerRig();
        rig.History.Loading.Add("sol");
        await rig.Enable("close-up", "8h");
        await rig.Scanner.StartAsync(CancellationToken.None);
        await rig.Scanner.ApplyAsync(new MarketPairs(Pairs), CancellationToken.None);

        // Two pairs in a minute: the third takes half a minute more.
        rig.Time.SetUtcNow(Ten.AddSeconds(65));
        await rig.Warm();

        Assert.That(rig.Scanner.WarmupProgress(), Is.EqualTo((2, 3, (TimeSpan?)TimeSpan.FromSeconds(30))));

        rig.History.Loading.Remove("sol");
        rig.Time.SetUtcNow(Ten.AddSeconds(130));
        await rig.Warm();

        Assert.That(rig.Scanner.WarmupProgress(), Is.EqualTo((3, 3, (TimeSpan?)null)), "the warm-up is over");
    }

    [Test]
    public async Task Before_a_pair_warmed_the_time_left_is_not_known()
    {
        var rig = new ScannerRig();
        foreach (Instrument pair in Pairs)
        {
            rig.History.Loading.Add(pair.Id);
        }

        await rig.Enable("close-up", "8h");
        await rig.Start();

        Assert.That(rig.Scanner.WarmupProgress(), Is.EqualTo((0, 3, (TimeSpan?)null)));
    }
}
