using Bybit.Net;

namespace ProfitFox.Infrastructure.Clients.Bybit;

/// <summary>
///     The section <c>Bybit</c> of the settings (change add-exchange-accounts, design.md, decision 4): <c>RestAddress</c> is
///     where the REST calls go instead of the live Bybit — a test puts its stub there; empty is the live Bybit.
/// </summary>
internal sealed record BybitSettings(string? RestAddress = null)
{
    public BybitEnvironment Environment()
    {
        return string.IsNullOrEmpty(RestAddress)
            ? BybitEnvironment.Live
            : BybitEnvironment.CreateCustom("profit-fox", RestAddress, BybitEnvironment.Live.SocketBaseAddress);
    }
}
