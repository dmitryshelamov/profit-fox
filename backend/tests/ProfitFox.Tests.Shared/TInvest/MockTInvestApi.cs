using Grpc.Core;

namespace ProfitFox.Tests.Shared.TInvest;

/// <summary>
///     An in-process stand-in for the gRPC server of T-Invest: a <see cref="CallInvoker" /> that answers unary calls with the handlers
///     a test registered by method, fails them with the <see cref="RpcException" /> a test asked for, answers server and duplex streams
///     from the scripts of a test (<see cref="OnServerStream{TRequest,TResponse}" />, <see cref="OnDuplex{TRequest,TResponse}" />), and
///     records every call with its request and metadata. No connection is opened; a method without a handler or a script fails with
///     <see cref="StatusCode.Unimplemented" />.
/// </summary>
public sealed class MockTInvestApi : CallInvoker
{
    private readonly Dictionary<string, Func<object, Metadata, Answer>> _handlers = [];
    private readonly Dictionary<string, object> _streams = [];
    private readonly List<RecordedCall> _calls = [];
    private readonly Lock _gate = new();

    /// <summary>Every call made so far, in order.</summary>
    public IReadOnlyList<RecordedCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Answers <paramref name="method" /> (the method name, such as <c>GetCandles</c>) with the response of the handler.</summary>
    public MockTInvestApi On<TRequest, TResponse>(string method, Func<TRequest, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        return On<TRequest, TResponse>(method, (request, _) => handler(request));
    }

    /// <summary>Answers <paramref name="method" /> with the response of the handler, which also sees the metadata of the call.</summary>
    public MockTInvestApi On<TRequest, TResponse>(string method, Func<TRequest, Metadata, TResponse> handler)
        where TRequest : class
        where TResponse : class
    {
        return Register<TRequest>(method, (request, metadata) => new Answer(handler(request, metadata), new Metadata(), new Metadata()));
    }

    /// <summary>Answers <paramref name="method" /> with a response and the headers and trailers of a test, such as the limit headers of T-Invest.</summary>
    public MockTInvestApi On<TRequest, TResponse>(string method, Func<TRequest, TResponse> handler, Metadata headers, Metadata? trailers = null)
        where TRequest : class
        where TResponse : class
    {
        return Register<TRequest>(method, (request, _) => new Answer(handler(request), headers, trailers ?? new Metadata()));
    }

    /// <summary>Fails <paramref name="method" /> with a status, as the server of T-Invest does: the detail is its error code, the trailers carry the rest.</summary>
    public MockTInvestApi Fail(string method, StatusCode status, string detail = "", Metadata? trailers = null)
    {
        return Register<object>(method, (_, _) => throw new RpcException(new Status(status, detail), trailers ?? new Metadata()));
    }

    /// <summary>Takes the answer of <paramref name="method" /> away, so the next call of it is <see cref="StatusCode.Unimplemented" />.</summary>
    public MockTInvestApi Forget(string method)
    {
        lock (_gate)
        {
            _handlers.Remove(method);
        }

        return this;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Metadata headers = options.Headers ?? new Metadata();
        Func<object, Metadata, Answer>? handler;

        lock (_gate)
        {
            _calls.Add(new RecordedCall(method.Name, request, headers, options.Deadline));
            _handlers.TryGetValue(method.Name, out handler);
        }

        if (handler is null)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, $"{method.FullName} has no handler in MockTInvestApi."));
        }

        Answer answer;

        try
        {
            answer = handler(request, headers);
        }
        catch (RpcException exception)
        {
            return new AsyncUnaryCall<TResponse>(
                Task.FromException<TResponse>(exception),
                Task.FromResult(new Metadata()),
                () => exception.Status,
                () => exception.Trailers,
                () => { });
        }

        return new AsyncUnaryCall<TResponse>(
            Task.FromResult((TResponse)answer.Response),
            Task.FromResult(answer.Headers),
            () => Status.DefaultSuccess,
            () => answer.Trailers,
            () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        return AsyncUnaryCall(method, host, options, request).ResponseAsync.GetAwaiter().GetResult();
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        MockTInvestStream<TRequest, TResponse> script = StreamOf(method, options, request);
        var call = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
        MockTInvestStreamCall<TRequest, TResponse> stream = script.Open(request, call.Token);

        return new AsyncServerStreamingCall<TResponse>(stream, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), call.Cancel);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        throw new NotSupportedException("MockTInvestApi answers unary, server and duplex calls only.");
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        MockTInvestStream<TRequest, TResponse> script = StreamOf(method, options, null);
        var call = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
        MockTInvestStreamCall<TRequest, TResponse> stream = script.Open(null, call.Token);

        return new AsyncDuplexStreamingCall<TRequest, TResponse>(stream, stream, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), call.Cancel);
    }

    /// <summary>Answers the server stream <paramref name="method" /> (such as <c>OrderStateStream</c>) from the script it returns.</summary>
    public MockTInvestStream<TRequest, TResponse> OnServerStream<TRequest, TResponse>(string method)
        where TRequest : class
        where TResponse : class
    {
        return Script<TRequest, TResponse>(method);
    }

    /// <summary>Answers the duplex stream <paramref name="method" /> (such as <c>MarketDataStream</c>) from the script it returns.</summary>
    public MockTInvestStream<TRequest, TResponse> OnDuplex<TRequest, TResponse>(string method)
        where TRequest : class
        where TResponse : class
    {
        return Script<TRequest, TResponse>(method);
    }

    private MockTInvestStream<TRequest, TResponse> Script<TRequest, TResponse>(string method)
        where TRequest : class
        where TResponse : class
    {
        var script = new MockTInvestStream<TRequest, TResponse>();

        lock (_gate)
        {
            _streams[method] = script;
        }

        return script;
    }

    private MockTInvestStream<TRequest, TResponse> StreamOf<TRequest, TResponse>(Method<TRequest, TResponse> method, CallOptions options, TRequest? request)
        where TRequest : class
        where TResponse : class
    {
        object? script;

        lock (_gate)
        {
            _calls.Add(new RecordedCall(method.Name, request!, options.Headers ?? new Metadata(), options.Deadline));
            _streams.TryGetValue(method.Name, out script);
        }

        return script as MockTInvestStream<TRequest, TResponse>
               ?? throw new RpcException(new Status(StatusCode.Unimplemented, $"{method.FullName} has no script in MockTInvestApi."));
    }

    private MockTInvestApi Register<TRequest>(string method, Func<TRequest, Metadata, Answer> handler)
        where TRequest : class
    {
        lock (_gate)
        {
            _handlers[method] = (request, metadata) => handler((TRequest)request, metadata);
        }

        return this;
    }

    private sealed record Answer(object Response, Metadata Headers, Metadata Trailers);

}