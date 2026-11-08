using Grpc.Core;

namespace ProfitFox.Tests.Shared.TInvest;

/// <summary>
///     The script of a streaming method of <see cref="MockTInvestApi" />: each call of the method opens a stream of its own
///     (<see cref="Streams" />), and the test writes the answers of a stream, ends it or fails it with a status; <see cref="Send" />,
///     <see cref="Complete" /> and <see cref="Fail" /> go to the stream opened last.
/// </summary>
public sealed class MockTInvestStream<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly Lock _gate = new();
    private readonly List<MockTInvestStreamCall<TRequest, TResponse>> _streams = [];

    /// <summary>Every stream opened so far, in order.</summary>
    public IReadOnlyList<MockTInvestStreamCall<TRequest, TResponse>> Streams
    {
        get
        {
            lock (_gate)
            {
                return [.. _streams];
            }
        }
    }

    /// <summary>How many times the method was called.</summary>
    public int Opens => Streams.Count;

    /// <summary>The request of each open of a server stream and every message the client wrote to a duplex one, of all the streams.</summary>
    public IReadOnlyList<TRequest> Requests => [.. Streams.SelectMany(stream => stream.Requests)];

    /// <summary>Whether the stream opened last is still open.</summary>
    public bool IsOpen => Streams.Count > 0 && Streams[^1].IsOpen;

    public void Send(TResponse response)
    {
        Last().Send(response);
    }

    public void Complete()
    {
        Last().Complete();
    }

    public void Fail(StatusCode status, string detail = "")
    {
        Last().Fail(status, detail);
    }

    internal MockTInvestStreamCall<TRequest, TResponse> Open(TRequest? request, CancellationToken cancellationToken)
    {
        var stream = new MockTInvestStreamCall<TRequest, TResponse>(request, cancellationToken);

        lock (_gate)
        {
            _streams.Add(stream);
        }

        return stream;
    }

    private MockTInvestStreamCall<TRequest, TResponse> Last()
    {
        IReadOnlyList<MockTInvestStreamCall<TRequest, TResponse>> streams = Streams;

        return streams.Count > 0 ? streams[^1] : throw new InvalidOperationException("No stream is open.");
    }
}