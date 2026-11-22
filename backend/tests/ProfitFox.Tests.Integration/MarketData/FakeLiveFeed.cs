using System.Threading.Channels;

using Grpc.Core;

namespace ProfitFox.Tests.Integration.MarketData;

/// <summary>
///     The live feed of one kind of the fake market-data service (change add-ideas-and-market, task 5.2): the items it has
///     stored, sent from <c>since</c> on a subscription, then the live ones a test pushes. As the real service, it catches up
///     no more than 24 hours by its clock and refuses a <c>since</c> further back with <c>INVALID_ARGUMENT</c>; a test cuts
///     its subscribers off with a status (<see cref="Fail" />) or as a dropped connection (<see cref="Drop" />).
/// </summary>
public sealed class FakeLiveFeed<T>(Func<T, DateTimeOffset> timeOf)
    where T : class
{
    /// <summary>
    ///     How far back the service catches up.
    /// </summary>
    public static readonly TimeSpan CatchUp = TimeSpan.FromHours(24);

    private readonly Lock _lock = new Lock();
    private readonly List<T> _stored = [];
    private readonly List<Subscriber> _subscribers = [];

    /// <summary>
    ///     The <c>since</c> of every subscription in the order they came; null for live only.
    /// </summary>
    public List<DateTimeOffset?> Subscriptions { get; } = [];

    /// <summary>
    ///     The clock of the service: what it holds goes back <see cref="CatchUp" /> from it.
    /// </summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>
    ///     How many subscribers are reading now.
    /// </summary>
    public int Readers
    {
        get
        {
            lock (_lock)
            {
                return _subscribers.Count;
            }
        }
    }

    /// <summary>
    ///     Stores an item without telling it live: what the service holds from before.
    /// </summary>
    public void Store(T item)
    {
        lock (_lock)
        {
            _stored.Add(item);
        }
    }

    /// <summary>
    ///     A live item: stored, and told to every subscriber now.
    /// </summary>
    public void Push(T item)
    {
        lock (_lock)
        {
            _stored.Add(item);
            foreach (Subscriber subscriber in _subscribers)
            {
                subscriber.Items.Writer.TryWrite(item);
            }
        }
    }

    /// <summary>
    ///     An item told live and not stored, as a forming candle; only to subscribers that asked for those.
    /// </summary>
    public void PushForming(T item)
    {
        lock (_lock)
        {
            foreach (Subscriber subscriber in _subscribers.Where(subscriber => subscriber.Forming))
            {
                subscriber.Items.Writer.TryWrite(item);
            }
        }
    }

    /// <summary>
    ///     Cuts every subscriber off with <paramref name="status" />: <c>UNAVAILABLE</c> when the service stops,
    ///     <c>RESOURCE_EXHAUSTED</c> for one that read too slowly.
    /// </summary>
    public void Fail(StatusCode status)
    {
        lock (_lock)
        {
            foreach (Subscriber subscriber in _subscribers)
            {
                subscriber.Items.Writer.TryWrite(new RpcException(new Status(status, $"the fake feed ends with {status}")));
            }
        }
    }

    /// <summary>
    ///     Breaks every subscription off as a dropped connection does.
    /// </summary>
    public void Drop()
    {
        lock (_lock)
        {
            foreach (Subscriber subscriber in _subscribers)
            {
                subscriber.Items.Writer.TryWrite(new IOException("the fake feed dropped the connection"));
            }
        }
    }

    /// <summary>
    ///     Serves a subscription: the stored items from <paramref name="since" />, oldest first, then the live ones, until
    ///     the caller ends the call or the feed cuts it off.
    /// </summary>
    public async Task Serve(DateTimeOffset? since, bool forming, IServerStreamWriter<T> stream, ServerCallContext context)
    {
        DateTimeOffset now = Time.GetUtcNow();
        lock (_lock)
        {
            Subscriptions.Add(since);
        }

        if (since is { } from && from < now - CatchUp)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"since {from:O} is further back than the service catches up ({CatchUp})"));
        }

        var subscriber = new Subscriber(Channel.CreateUnbounded<object>(), forming);
        T[] backlog;
        lock (_lock)
        {
            backlog = since is { } start ? [.. _stored.Where(item => timeOf(item) >= start).OrderBy(timeOf)] : [];
            _subscribers.Add(subscriber);
        }

        try
        {
            await context.WriteResponseHeadersAsync(new Metadata());
            foreach (T item in backlog)
            {
                await stream.WriteAsync(item);
            }

            await foreach (object item in subscriber.Items.Reader.ReadAllAsync(context.CancellationToken))
            {
                switch (item)
                {
                    case IOException:
                        // A dropped connection: the client reads a broken stream.
                        context.GetHttpContext().Abort();
                        return;
                    case Exception end:
                        throw end;
                    default:
                        await stream.WriteAsync((T)item);
                        break;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(subscriber);
            }
        }
    }

    private sealed record Subscriber(Channel<object> Items, bool Forming);
}
