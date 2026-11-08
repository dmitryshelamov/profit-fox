using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.Scanner;
using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Scanner.Ideas;

/// <summary>
///     The profile of a turn of the replay (decision 1 of speed-up-ideas-replay): 90 days of synthetic minutes of one pair, deterministic by a
///     seed, whose regimes (calm stretches with a lean of volume, trends, ranges, climaxes) fire every setup, replayed through
///     <see cref="IdeasReplay" /> over repositories in memory. It prints the timings of the turn as the journal gets them, the seconds per
///     pair-day and the paths open at the end of each day, and keeps the signals of the turn in a snapshot: the first run writes it, every
///     later one compares, so an optimization that changed a signal fails here. Explicit: it runs on demand, never in <c>dotnet test</c>.
///     <c>PROFITFOX_REPLAY_SNAPSHOT</c> names the file of the snapshot; without it, one in the temp folder.
/// </summary>
[Explicit("The profile of a turn of the replay: prints timings and keeps a snapshot of its signals.")]
public class IdeasReplayProfileTests
{
    private const int Seed = 20261108;
    private const int Days = 90;
    private static readonly Instrument Pair = new Instrument(MarketCategory.Linear, "PROFUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly DateTimeOffset End = Start.AddDays(Days);

    // The warm-up reads 90 days of candles and 220 candles of 4 hours before the start of the turn.
    private static readonly DateTimeOffset HistoryStart = Start.AddDays(-130);
    private const decimal Tick = 0.0001m;

    [Test]
    public async Task Profile_of_a_turn_over_90_days()
    {
        IReadOnlyList<IdeaCandle> minutes = Minutes(Seed, 1.2345m, HistoryStart, End);
        IReadOnlyList<IdeaCandle> btc = Minutes(Seed + 1, 42_000m, Start.AddDays(-1), End);
        var written = new Dictionary<(IdeaKey, DateTimeOffset), IdeaSignal>();
        var openByDay = new SortedDictionary<DateTimeOffset, int>();
        (Instrument Instrument, int Days, TimeSpan Engine, TimeSpan Reads, TimeSpan Writes)? replayed = null;

        ICandleRepository candles = Substitute.For<ICandleRepository>();
        candles.GetAsync(Pair, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Stored(minutes, HistoryStart, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        candles.GetAsync(Btc, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Stored(btc, Start.AddDays(-1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        candles.GetAggregatedAsync(Pair, Arg.Any<Interval>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => IdeaCandles.FromMinutes(call.ArgAt<Interval>(1), minutes, call.ArgAt<DateTimeOffset>(2), call.ArgAt<DateTimeOffset>(3)));
        IIdeaSignalRepository signals = Substitute.For<IIdeaSignalRepository>();
        signals.UpsertAsync(Arg.Any<IReadOnlyCollection<IdeaSignal>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            foreach (IdeaSignal signal in call.Arg<IReadOnlyCollection<IdeaSignal>>())
            {
                written[(signal.Key, signal.Path.FiredAt)] = signal;
            }

            return Task.CompletedTask;
        });
        IIdeaReplayRepository cursors = Substitute.For<IIdeaReplayRepository>();
        cursors.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Instrument, IdeaReplayCursor>());
        cursors.SetAsync(Arg.Any<Instrument>(), Arg.Any<int>(), Arg.Any<IdeaReplayCursor>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            IdeaReplayCursor cursor = call.ArgAt<IdeaReplayCursor>(2);
            openByDay[cursor.ReplayedUntil] = cursor.Open.Count;

            return Task.CompletedTask;
        });
        IFundingRateRepository funding = Substitute.For<IFundingRateRepository>();
        funding.GetAsync(Pair, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Funding(call.ArgAt<DateTimeOffset>(1), call.ArgAt<DateTimeOffset>(2), call.ArgAt<int>(3)));
        IScannerJournal journal = Substitute.For<IScannerJournal>();
        journal.When(each => each.Replayed(Arg.Any<Instrument>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>()))
            .Do(call => replayed = (call.ArgAt<Instrument>(0), call.ArgAt<int>(1), call.ArgAt<TimeSpan>(2), call.ArgAt<TimeSpan>(3), call.ArgAt<TimeSpan>(4)));
        var market = new ScannerMarket();
        market.Keep([new ScannerPair(Pair, true, HistoryFrom: Start, CoveredUntil: End, TickSize: Tick)]);
        var replay = new IdeasReplay(
            candles,
            signals,
            cursors,
            Substitute.For<IIdeaVolatilityRepository>(),
            funding,
            market,
            new IdeasSettings(),
            new ScannerSettings(),
            new IdeasEngineVersion(IdeasEngineVersion.Current),
            new ScannerPace(),
            journal,
            new FakeTimeProvider(End));

        var whole = Stopwatch.StartNew();
        Task? turn = await replay.StartAsync(CancellationToken.None);
        turn.ShouldNotBeNull();
        await turn;
        whole.Stop();

        replayed.ShouldNotBeNull();
        (Instrument instrument, int days, TimeSpan engine, TimeSpan reads, TimeSpan writes) = replayed.Value;
        instrument.ShouldBe(Pair);
        days.ShouldBe(Days);
        TestContext.Out.WriteLine($"Turn of {days} days in {whole.Elapsed.TotalSeconds:F2} s: engine {engine.TotalMilliseconds:F0} ms, reads {reads.TotalMilliseconds:F0} ms, writes {writes.TotalMilliseconds:F0} ms.");
        TestContext.Out.WriteLine($"{whole.Elapsed.TotalSeconds / days:F3} s per pair-day; {written.Count} signals, {openByDay.Values.Max()} paths open at the most, {openByDay.Values.Average():F0} on average.");
        TestContext.Out.WriteLine("Open paths at the end of each day: " + string.Join(' ', openByDay.Values));
        IdeaSetup[] setups = [.. written.Values.Select(signal => signal.Setup).Distinct().Order()];
        TestContext.Out.WriteLine("Setups fired: " + string.Join(", ", setups));
        setups.ShouldBe([.. Enum.GetValues<IdeaSetup>().Order()], "Every setup fires on the synthetic history.");

        string[] lines = [.. written.Values.Select(Line).Order(StringComparer.Ordinal)];
        TestContext.Out.WriteLine($"Fingerprint of the signals: {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))}");
        string path = Environment.GetEnvironmentVariable("PROFITFOX_REPLAY_SNAPSHOT") ?? Path.Combine(Path.GetTempPath(), "profit-fox-replay-profile-signals.txt");

        if (File.Exists(path))
        {
            string[] snapshot = await File.ReadAllLinesAsync(path);
            TestContext.Out.WriteLine($"Compared with the snapshot of {snapshot.Length} signals at {path}.");
            lines.ShouldBe(snapshot, "The signals of the turn differ from the snapshot taken before the optimization.");
        }
        else
        {
            await File.WriteAllLinesAsync(path, lines);
            TestContext.Out.WriteLine($"Snapshot of {lines.Length} signals written to {path}.");
        }
    }

    private static string Line(IdeaSignal signal)
    {
        return JsonSerializer.Serialize(new { signal.Setup, signal.Path, signal.Move, signal.VolumeRatio, signal.PairChange, signal.BtcChange, signal.Funding });
    }

    // The minutes go without a gap from the first, so a range is a slice.
    private static IReadOnlyList<Candle> Stored(IReadOnlyList<IdeaCandle> minutes, DateTimeOffset first, DateTimeOffset from, DateTimeOffset to)
    {
        int start = Math.Clamp((int)(from - first).TotalMinutes, 0, minutes.Count);
        int end = Math.Clamp((int)(to - first).TotalMinutes, 0, minutes.Count);

        return [.. minutes.Skip(start).Take(end - start).Select(minute => new Candle(minute.OpenTime, new Price(minute.Open), new Price(minute.High), new Price(minute.Low), new Price(minute.Close), new Volume(1m), new Volume(minute.Volume)))];
    }

    // A settlement every 8 hours, the rate swinging with the hour.
    private static IReadOnlyList<FundingRatePoint> Funding(DateTimeOffset from, DateTimeOffset to, int limit)
    {
        var points = new List<FundingRatePoint>();

        for (DateTimeOffset time = Floor(from, TimeSpan.FromHours(8)); time < to && points.Count < limit; time = time.AddHours(8))
        {
            if (time >= from)
            {
                points.Add(new FundingRatePoint(time, 0.0001m * (1 + (time.Hour / 8 % 3))));
            }
        }

        return points;
    }

    private static DateTimeOffset Floor(DateTimeOffset time, TimeSpan step)
    {
        return new DateTimeOffset(time.UtcTicks - (time.UtcTicks % step.Ticks), TimeSpan.Zero);
    }

    private enum Regime
    {
        CalmBuying,
        CalmSelling,
        Up,
        Down,
        Range,
        Climax
    }

    /// <summary>
    ///     Minutes of a pair by a seeded random walk over regimes of 30 minutes to 12 hours: calm stretches with a lean of volume to one side (a
    ///     squeeze, an accumulation or a distribution), trends with volume (impulses, drops, breakouts, new extremes, RSI beyond its bounds),
    ///     ranges with wicks (sweeps of zones) and short climaxes of volume with long wicks (exhaustions, volume without a move).
    /// </summary>
    private static IReadOnlyList<IdeaCandle> Minutes(int seed, decimal start, DateTimeOffset from, DateTimeOffset to)
    {
        var random = new Random(seed);
        var minutes = new List<IdeaCandle>((int)(to - from).TotalMinutes);
        decimal price = start;
        Regime regime = Regime.Range;
        int left = 0;

        for (DateTimeOffset time = from; time < to; time = time.AddMinutes(1))
        {
            if (left == 0)
            {
                regime = (Regime)random.Next(Enum.GetValues<Regime>().Length);
                left = regime == Regime.Climax ? random.Next(20, 60) : random.Next(30, 720);
            }

            left--;
            double drift = regime switch
            {
                Regime.Up => 0.0004,
                Regime.Down => -0.0004,
                _ => 0
            };
            double noise = regime switch
            {
                Regime.CalmBuying or Regime.CalmSelling => 0.00015,
                Regime.Range => 0.0006,
                Regime.Climax => 0.0012,
                _ => 0.0008
            };
            double change = drift + (noise * Gaussian(random));
            decimal open = price;
            decimal close = Round(price * (1 + (decimal)change));
            decimal range = Math.Abs(close - open);
            double wick = regime == Regime.Climax ? 2.5 : regime == Regime.Range ? 1.2 : 0.4;
            decimal high = Round(Math.Max(open, close) + (range * (decimal)(wick * random.NextDouble())) + (price * 0.00005m));
            decimal low = Round(Math.Min(open, close) - (range * (decimal)(wick * random.NextDouble())) - (price * 0.00005m));
            double volume = regime switch
            {
                Regime.CalmBuying => close > open ? 1.6 : 0.6,
                Regime.CalmSelling => close < open ? 1.6 : 0.6,
                Regime.Up or Regime.Down => 2.2,
                Regime.Climax => 6,
                _ => 1
            };
            volume *= Math.Exp(0.4 * Gaussian(random));

            if (random.NextDouble() < 0.01)
            {
                volume *= 5;
            }

            minutes.Add(new IdeaCandle(time, open, high, low, close, Math.Round((decimal)(volume * 1000), 2)));
            price = close;
        }

        return minutes;
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 4);
    }

    // Box–Muller.
    private static double Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}