using System.Threading.Channels;

using Grpc.Core;

namespace ProfitFox.Tests.Shared.TInvest;

/// <summary>One open of a streaming method of <see cref="MockTInvestApi" />: what the client sent on it, and the answers a test writes to it.</summary>
public sealed class MockTInvestStreamCall<TRequest, TResponse> : IAsyncStreamReader<TResponse>, IClientStreamWriter<TRequest>
    where TRequest : class
    where TResponse : class
{
    private readonly Lock _gate = new();
    private readonly List<TRequest> _requests = [];
    private readonly Channel<TResponse> _answers = Channel.CreateUnbounded<TResponse>();
    private readonly CancellationToken _call;

    internal MockTInvestStreamCall(TRequest? request, CancellationToken call)
    {
        _call = call;

        if (request is not null)
        {
            _requests.Add(request);
        }

        // The client gives the call up with its token or by disposing it: the stream ends for it.
        call.Register(() => _answers.Writer.TryComplete());
    }

    /// <summary>The request of a server stream, or every message the client wrote to a duplex one.</summary>
    public IReadOnlyList<TRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Neither ended by the test nor given up by the client.</summary>
    public bool IsOpen => !_answers.Reader.Completion.IsCompleted;

    public TResponse Current { get; private set; } = null!;

    public WriteOptions? WriteOptions { get; set; }

    public void Send(TResponse response)
    {
        _answers.Writer.TryWrite(response);
    }

    /// <summary>Ends the stream as the server does.</summary>
    public void Complete()
    {
        _answers.Writer.TryComplete();
    }

    /// <summary>Fails the stream with a status, as a broken connection or a refusal of the server.</summary>
    public void Fail(StatusCode status, string detail = "")
    {
        _answers.Writer.TryComplete(new RpcException(new Status(status, detail)));
    }

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        using var both = CancellationTokenSource.CreateLinkedTokenSource(_call, cancellationToken);

        try
        {
            if (await _answers.Reader.WaitToReadAsync(both.Token) && _answers.Reader.TryRead(out TResponse? item))
            {
                Current = item;

                return true;
            }
        }
        catch (ChannelClosedException exception) when (exception.InnerException is RpcException failure)
        {
            throw failure;
        }
        catch (RpcException)
        {
            throw;
        }

        _call.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();

        return false;
    }

    public Task WriteAsync(TRequest message)
    {
        lock (_gate)
        {
            _requests.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task CompleteAsync()
    {
        return Task.CompletedTask;
    }
}