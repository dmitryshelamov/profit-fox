using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using Grpc.Core;
using Grpc.Net.Client;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The one gRPC channel of the process to T-Invest, on a <see cref="SocketsHttpHandler" /> whose server certificate check is the
///     system trust or the pinned chain of <see cref="TInvestCertificates" />. Nothing connects until the first call.
/// </summary>
internal sealed class TInvestChannel : ITInvestCallInvokers, IDisposable
{
    private readonly GrpcChannel _channel;

    public TInvestChannel(TInvestSettings settings, TInvestCertificates certificates)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, chain, errors) => certificates.Accept(
                    certificate switch
                    {
                        X509Certificate2 loaded => loaded,
                        { } raw => X509CertificateLoader.LoadCertificate(raw.GetRawCertData()),
                        null => null
                    },
                    chain is null ? [] : [.. chain.ChainElements.Skip(1).Select(element => element.Certificate)],
                    errors)
            }
        };
        // A call cancelled by its token is an OperationCanceledException, as everywhere else, not an RpcException of the exchange.
        _channel = GrpcChannel.ForAddress(settings.Endpoint, new GrpcChannelOptions { HttpHandler = handler, ThrowOperationCanceledOnCancellation = true });
        Invoker = _channel.CreateCallInvoker();
    }

    public CallInvoker Invoker { get; }

    public void Dispose()
    {
        _channel.Dispose();
    }
}