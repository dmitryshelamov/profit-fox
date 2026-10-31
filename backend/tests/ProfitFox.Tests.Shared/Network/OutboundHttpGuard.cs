using System.Net;

namespace ProfitFox.Tests.Shared.Network;

/// <summary>
///     Sends every outbound HTTP request that a test did not replace to a dead local proxy, so it fails instead of reaching
///     a real exchange. SDKs such as Bybit.Net configure their own handler, so a missed replacement is easy to overlook.
/// </summary>
public static class OutboundHttpGuard
{
    public static void Install()
    {
        HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:9", BypassOnLocal: true);
    }
}