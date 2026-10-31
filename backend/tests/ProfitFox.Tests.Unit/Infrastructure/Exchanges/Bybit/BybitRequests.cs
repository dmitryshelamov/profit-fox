using System.Web;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Request path with its query parameters sorted, so tests do not depend on the order the SDK writes them in.</summary>
public static class BybitRequests
{
    public static string Describe(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return uri.AbsolutePath + " " + string.Join("&", query.AllKeys.Order().Select(key => $"{key}={query[key]}"));
    }

    public static string Describe(string pathAndQuery) => Describe(new Uri(new Uri("https://bybit.test"), pathAndQuery));
}
