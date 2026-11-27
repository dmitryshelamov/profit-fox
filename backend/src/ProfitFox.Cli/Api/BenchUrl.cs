using Microsoft.Extensions.Configuration;

namespace ProfitFox.Cli.Api;

/// <summary>
///     Where the bench service is: <c>--bench</c>, else <c>Bench:Url</c> of the <c>appsettings.json</c> next to the binary or of the
///     environment (<c>Bench__Url</c>), else <see cref="Default" />.
/// </summary>
internal static class BenchUrl
{
    public const string Default = "http://localhost:5240";

    public static Uri Resolve(string? option)
    {
        string? url = option;
        if (url is null)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            url = configuration["Bench:Url"];
        }

        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(url) ? Default : url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new UsageException($"the bench address '{url}' is not an http address.");
        }

        return uri;
    }
}
