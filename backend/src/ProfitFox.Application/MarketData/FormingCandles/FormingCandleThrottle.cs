using ProfitFox.Application.Ports.MarketData;

namespace ProfitFox.Application.MarketData.FormingCandles;

/// <summary>
///     Lets the updates of one candle series through at most once a second. The closing of a candle and the first candle of a
///     new interval go at once; otherwise the newest update waits for the end of the second and goes alone. An update older than
///     the newest taken is dropped. <paramref name="send" /> runs under the lock of the throttle, so it must not block.
/// </summary>
public sealed class FormingCandleThrottle(TimeProvider time, Action<FormingCandle> send) : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new Lock();
    private ITimer? _timer;
    private FormingCandle? _sent;
    private DateTimeOffset _sentAt;
    private FormingCandle? _held;
    private FormingCandle? _latest;

    /// <summary>The newest update taken, sent or held.</summary>
    public FormingCandle? Latest
    {
        get
        {
            lock (_lock)
            {
                return _latest;
            }
        }
    }

    public void Offer(FormingCandle candle)
    {
        lock (_lock)
        {
            if (_latest is not null && (candle.OpenTime, candle.ObservedAt).CompareTo((_latest.OpenTime, _latest.ObservedAt)) < 0)
            {
                return;
            }

            _latest = candle;
            DateTimeOffset now = time.GetUtcNow();

            if (_sent is null || candle.IsClosed || candle.OpenTime != _sent.OpenTime || now - _sentAt >= Period)
            {
                Send(candle, now);

                return;
            }

            _held = candle;
            _timer ??= time.CreateTimer(_ => SendHeld(), null, _sentAt + Period - now, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _held = null;
        }
    }

    private void SendHeld()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            if (_held is { } held)
            {
                Send(held, time.GetUtcNow());
            }
        }
    }

    private void Send(FormingCandle candle, DateTimeOffset now)
    {
        _timer?.Dispose();
        _timer = null;
        _held = null;
        _sent = candle;
        _sentAt = now;
        send(candle);
    }
}