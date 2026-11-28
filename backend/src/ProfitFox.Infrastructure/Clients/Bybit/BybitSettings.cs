using Bybit.Net;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The section <c>Bybit</c> of the settings (change add-exchange-accounts, design.md, decision 4): <c>RestAddress</c> is
///     where the REST calls go instead of the live Bybit — a test puts its stub there; empty is the live Bybit. The streams of
///     change take-idea-trades go to <c>SocketAddress</c>, and to the live Bybit when it is empty.
/// </summary>
internal sealed record BybitSettings(string? RestAddress = null, string? SocketAddress = null)
{
    public BybitEnvironment Environment()
    {
        return string.IsNullOrEmpty(RestAddress) && string.IsNullOrEmpty(SocketAddress)
            ? BybitEnvironment.Live
            : BybitEnvironment.CreateCustom(
                "profit-fox",
                string.IsNullOrEmpty(RestAddress) ? BybitEnvironment.Live.RestBaseAddress : RestAddress,
                string.IsNullOrEmpty(SocketAddress) ? BybitEnvironment.Live.SocketBaseAddress : SocketAddress);
    }
}
