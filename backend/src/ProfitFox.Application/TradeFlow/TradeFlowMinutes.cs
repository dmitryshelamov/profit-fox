using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.TradeFlow;

/// <summary>
///     The minutes of the trade flow in memory (decision 6 of collect-trade-flow): the trades of a pair add up in the minute of their time, and
///     <see cref="CloseMargin" /> after the end of a minute it goes to storage in one batch with the other pairs. A written minute stays until
///     <see cref="LateLimit" /> after its end, so a late trade writes it again whole; a later trade is dropped, and the rebuild of the day brings
///     it back. One object serves one session of the collector; the stream adds trades while the lane of writes takes the closed minutes.
/// </summary>
public sealed class TradeFlowMinutes(TimeProvider time)
{
    /// <summary>How long after the end of a minute it is written, so the trades of its last moment arrive first.</summary>
    public static readonly TimeSpan CloseMargin = TimeSpan.FromSeconds(5);

    /// <summary>How long after the end of a minute a late trade still writes it again.</summary>
    public static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Length = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<(Instrument Instrument, DateTimeOffset OpenTime), TradeFlowMinute> _minutes = [];
    private readonly HashSet<(Instrument Instrument, DateTimeOffset OpenTime)> _unwritten = [];

    /// <summary>How many minutes of all pairs are in memory.</summary>
    public int Kept
    {
        get
        {
            lock (_lock)
            {
                return _minutes.Count;
            }
        }
    }

    public void Add(Instrument instrument, TradeEvent trade)
    {
        var started = TradeFlowMinute.Start(trade);
        (Instrument, DateTimeOffset) key = (instrument, started.OpenTime);

        if (started.OpenTime + Length + LateLimit <= time.GetUtcNow())
        {
            return;
        }

        lock (_lock)
        {
            _minutes[key] = _minutes.TryGetValue(key, out TradeFlowMinute? minute) ? minute.Add(trade) : started;
            _unwritten.Add(key);
        }
    }

    /// <summary>The minutes that ended <see cref="CloseMargin" /> ago or more and are not written yet, or have changed since they were.</summary>
    public IReadOnlyList<(Instrument Instrument, TradeFlowMinute Minute)> TakeClosed()
    {
        DateTimeOffset now = time.GetUtcNow();

        lock (_lock)
        {
            (Instrument Instrument, TradeFlowMinute Minute)[] closed =
                [.. _unwritten.Where(key => key.OpenTime + Length + CloseMargin <= now).Select(key => (key.Instrument, _minutes[key]))];

            foreach ((Instrument instrument, TradeFlowMinute minute) in closed)
            {
                _unwritten.Remove((instrument, minute.OpenTime));
            }

            foreach ((Instrument, DateTimeOffset) old in _minutes.Keys.Where(key => key.OpenTime + Length + LateLimit <= now).ToArray())
            {
                _minutes.Remove(old);
            }

            return closed;
        }
    }
}