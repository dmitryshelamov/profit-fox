using System.Globalization;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Infrastructure.Exchanges.Retry;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Every unary call to T-Invest: the limiter lets it go, the metadata (<c>authorization: Bearer</c>, <c>x-app-name</c>) and the
///     deadline are set, the answer is counted in the metrics and its limit headers read, and an <see cref="RpcException" /> becomes an
///     exception of Application: <c>UNAUTHENTICATED</c> and <c>PERMISSION_DENIED</c> a rejected key; <c>RESOURCE_EXHAUSTED</c> a pause of
///     the group until <c>x-ratelimit-reset</c> and a rate limit; <c>UNAVAILABLE</c>, <c>INTERNAL</c>, <c>ABORTED</c> and
///     <c>DEADLINE_EXCEEDED</c> an unavailable exchange, retried by the ladder of the <see cref="ExchangeResilienceExecutor" /> of T-Invest
///     (its circuit breaker is apart from the one of Bybit) where the caller asks for retries; the rest a refusal with the error code of
///     the detail. No text of the broker reaches a message.
/// </summary>
internal sealed class TInvestCall(
    ITInvestCallInvokers invokers,
    TInvestRequestLimiter limiter,
    TInvestMetrics metrics,
    [FromKeyedServices(Exchange.TInvest)] ExchangeResilienceExecutor resilience,
    TInvestSettings settings,
    TimeProvider time)
{
    private static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxPause = TimeSpan.FromHours(1);

    /// <summary>A single attempt: a person waits for the answer.</summary>
    /// <exception cref="ExchangeKeyRejectedException">T-Invest does not accept the token.</exception>
    /// <exception cref="ExchangeRateLimitedException">The token went over the limit of the group.</exception>
    /// <exception cref="ExchangeUnavailableException">T-Invest did not answer.</exception>
    /// <exception cref="ExchangeException">T-Invest refused the call.</exception>
    public async Task<TResponse> OnceAsync<TResponse>(string token, Func<CallInvoker, AsyncUnaryCall<TResponse>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteOnceAsync(async attempt => await AttemptAsync(token, call, attempt), cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw exception.Unavailable($"T-Invest is unavailable: {exception.Message}");
        }
    }

    /// <summary>The attempt repeated after 1, 2, 4, 8 and 16 s while T-Invest does not answer, as a Worker loads history.</summary>
    /// <exception cref="ExchangeKeyRejectedException">T-Invest does not accept the token.</exception>
    /// <exception cref="ExchangeRateLimitedException">The token went over the limit of the group.</exception>
    /// <exception cref="ExchangeUnavailableException">T-Invest did not answer after the retries.</exception>
    /// <exception cref="ExchangeException">T-Invest refused the call.</exception>
    public async Task<TResponse> WithRetriesAsync<TResponse>(string token, Func<CallInvoker, AsyncUnaryCall<TResponse>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await resilience.ExecuteWithRetriesAsync(async attempt => await AttemptAsync(token, call, attempt), cancellationToken);
        }
        catch (TransientExchangeException exception)
        {
            throw exception.Unavailable($"T-Invest request failed after {resilience.RetryCount} retries: {exception.Message}");
        }
    }

    private async Task<TResponse> AttemptAsync<TResponse>(string token, Func<CallInvoker, AsyncUnaryCall<TResponse>> call, CancellationToken cancellationToken)
    {
        var guarded = new GuardedInvoker(this, token, cancellationToken);

        using AsyncUnaryCall<TResponse> unary = call(guarded);

        return await unary.ResponseAsync;
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request,
        string token,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        TInvestMethodGroup group = TInvestMethods.GroupOf(method.ServiceName);
        await limiter.AcquireAsync(token, group, cancellationToken);
        var headers = new Metadata { { "authorization", $"Bearer {token}" }, { "x-app-name", settings.AppName } };
        CallOptions sent = options
            .WithHeaders(headers)
            .WithDeadline(time.GetUtcNow().UtcDateTime + settings.RequestTimeout)
            .WithCancellationToken(cancellationToken);

        try
        {
            using AsyncUnaryCall<TResponse> call = invokers.Invoker.AsyncUnaryCall(method, host, sent, request);
            TResponse response = await call.ResponseAsync;
            metrics.Observe(group, await call.ResponseHeadersAsync);
            metrics.Count(group, method.Name, "ok");

            return response;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
        {
            // The caller gave up, not the exchange: neither counted nor translated.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (RpcException exception)
        {
            metrics.Count(group, method.Name, exception.StatusCode == StatusCode.ResourceExhausted ? "rate_limited" : "error");

            throw Translate(exception, token, group);
        }
    }

    private Exception Translate(RpcException exception, string token, TInvestMethodGroup group)
    {
        switch (exception.StatusCode)
        {
            case StatusCode.Unauthenticated or StatusCode.PermissionDenied:
                return new ExchangeKeyRejectedException("T-Invest does not accept the token.");
            case StatusCode.ResourceExhausted:
                TimeSpan pause = ResetIn(exception.Trailers);
                limiter.Pause(token, group, time.GetUtcNow() + pause);

                return new ExchangeRateLimitedException($"T-Invest limited the {group.ToCode()} requests of the token for {pause.TotalSeconds:0} s.");
            case StatusCode.Unavailable or StatusCode.Internal or StatusCode.Aborted or StatusCode.DeadlineExceeded:
                return new TransientExchangeException(exception.StatusCode.ToString());
            case StatusCode.NotFound:
                return new TInvestNotFoundException(CodeOf(exception), Refusal(exception));
            default:
                return new TInvestRefusedException(CodeOf(exception), Refusal(exception));
        }
    }

    // The detail of a refusal of T-Invest is its error code; any text of the broker stays in the trailers and out of the message.
    private static int? CodeOf(RpcException exception)
    {
        return int.TryParse(exception.Status.Detail, NumberStyles.None, CultureInfo.InvariantCulture, out int code) ? code : null;
    }

    private static string Refusal(RpcException exception)
    {
        return CodeOf(exception) is { } code
            ? $"T-Invest refused the request with code {code}."
            : $"T-Invest refused the request ({exception.StatusCode}).";
    }

    private static TimeSpan ResetIn(Metadata trailers)
    {
        return trailers.GetValue("x-ratelimit-reset") is { } value
               && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
               && seconds >= 1
               && TimeSpan.FromSeconds(seconds) <= MaxPause
            ? TimeSpan.FromSeconds(seconds)
            : DefaultPause;
    }

    /// <summary>The invoker a generated client of one attempt calls: it runs the pipeline of the attempt around the real invoker.</summary>
    private sealed class GuardedInvoker(TInvestCall owner, string token, CancellationToken cancellationToken) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Task<TResponse> response = owner.SendAsync(method, host, options, request, token, cancellationToken);

            return new AsyncUnaryCall<TResponse>(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException("Calls to T-Invest are asynchronous.");
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException("This change makes unary calls to T-Invest only.");
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException("This change makes unary calls to T-Invest only.");
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException("This change makes unary calls to T-Invest only.");
        }
    }
}