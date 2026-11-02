namespace ProfitFox.Infrastructure.Exchanges.Bybit;

/// <summary>The <c>Bybit</c> section of the service settings: where the app reaches Bybit.</summary>
/// <param name="RestAddress">The address REST calls go to instead of the live Bybit, such as <c>http://127.0.0.1:5296</c>; without it, the live Bybit.</param>
/// <param name="SocketAddress">The address streams connect to instead of the live Bybit, such as <c>ws://127.0.0.1:5297</c>; without it, the live Bybit.</param>
internal sealed record BybitSettings(string? RestAddress = null, string? SocketAddress = null)
{
    public global::Bybit.Net.BybitEnvironment Environment()
    {
        return string.IsNullOrEmpty(RestAddress) && string.IsNullOrEmpty(SocketAddress)
            ? global::Bybit.Net.BybitEnvironment.Live
            : global::Bybit.Net.BybitEnvironment.CreateCustom(
                "profit-fox",
                string.IsNullOrEmpty(RestAddress) ? global::Bybit.Net.BybitEnvironment.Live.RestBaseAddress : RestAddress,
                string.IsNullOrEmpty(SocketAddress) ? global::Bybit.Net.BybitEnvironment.Live.SocketBaseAddress : SocketAddress);
    }
}