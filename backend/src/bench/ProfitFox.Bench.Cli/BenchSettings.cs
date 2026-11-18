using System.Text.Json;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     Where the market-data service is: <c>appsettings.json</c> next to the tool, overridden by the environment variables
///     <c>BENCH_MARKETDATA_GRPC</c> and <c>BENCH_MARKETDATA_REST</c>.
/// </summary>
public sealed record BenchSettings(string GrpcUrl, string RestUrl)
{
    public static BenchSettings Load(string folder)
    {
        string grpc = "";
        string rest = "";
        string path = Path.Combine(folder, "appsettings.json");
        if (File.Exists(path))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("MarketData", out JsonElement marketData))
            {
                grpc = marketData.TryGetProperty("GrpcUrl", out JsonElement grpcUrl) ? grpcUrl.GetString() ?? "" : "";
                rest = marketData.TryGetProperty("RestUrl", out JsonElement restUrl) ? restUrl.GetString() ?? "" : "";
            }
        }

        grpc = Environment.GetEnvironmentVariable("BENCH_MARKETDATA_GRPC") is { Length: > 0 } grpcOverride ? grpcOverride : grpc;
        rest = Environment.GetEnvironmentVariable("BENCH_MARKETDATA_REST") is { Length: > 0 } restOverride ? restOverride : rest;
        if (grpc.Length == 0 || rest.Length == 0)
        {
            throw new InvalidOperationException("The addresses of the market-data service are not set (MarketData:GrpcUrl, MarketData:RestUrl).");
        }

        return new BenchSettings(grpc, rest);
    }
}
