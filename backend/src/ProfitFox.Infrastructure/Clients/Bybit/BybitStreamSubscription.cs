using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using ProfitFox.Application.Ports.Exchange;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The subscriptions of one stream with the events of the package about their connection told to the port once each
///     (change take-idea-trades, design.md, decision 3): the connection is lost — the package reconnects and authorizes again
///     by itself —, restored with the subscriptions, or closed for good; a failed resubscription is closed too, so the stream
///     is opened anew. The topics of a stream share one connection, and each of them tells the same event: only a change is
///     passed on. Disposing of it closes the subscriptions and the client.
/// </summary>
internal sealed class BybitStreamSubscription : IExchangeSubscription
{
    private readonly IDisposable _client;
    private readonly Action<ExchangeStreamState> _onState;
    private readonly List<UpdateSubscription> _subscriptions = [];
    private readonly Lock _gate = new Lock();
    private ExchangeStreamState? _last;
    private bool _disposed;

    public BybitStreamSubscription(IDisposable client, Action<ExchangeStreamState> onState)
    {
        _client = client;
        _onState = onState;
    }

    public void Add(UpdateSubscription subscription)
    {
        _subscriptions.Add(subscription);
        subscription.ConnectionLost += () => Tell(ExchangeStreamState.Lost);
        subscription.ConnectionRestored += _ => Tell(ExchangeStreamState.Restored);
        subscription.ConnectionClosed += () => Tell(ExchangeStreamState.Closed);
        subscription.ResubscribingFailed += (Error _) => Tell(ExchangeStreamState.Closed);
    }

    /// <summary>
    ///     Passes the state on when it is a change; after <see cref="ExchangeStreamState.Closed" /> nothing more is passed.
    /// </summary>
    public void Tell(ExchangeStreamState state)
    {
        lock (_gate)
        {
            if (_disposed || _last == state || _last == ExchangeStreamState.Closed)
            {
                return;
            }

            _last = state;
        }

        _onState(state);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        foreach (UpdateSubscription subscription in _subscriptions)
        {
            try
            {
                await subscription.CloseAsync();
            }
            catch (Exception)
            {
                // A connection already gone has nothing to close.
            }
        }

        _client.Dispose();
    }
}
