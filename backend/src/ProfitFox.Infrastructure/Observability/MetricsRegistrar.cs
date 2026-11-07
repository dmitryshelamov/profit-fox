using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace ProfitFox.Infrastructure.Observability;

/// <summary>
///     Pushes the metrics of the service over OTLP (<c>http/protobuf</c>, cumulative) to the receiver of the settings, under the
///     <c>service.name</c> its host gives, which Prometheus turns into the <c>job</c> label.
/// </summary>
internal static class MetricsRegistrar
{
    public static void Configure(IConfiguration configuration, IServiceCollection services, string serviceName)
    {
        MetricsSettings settings = configuration.GetSection("Metrics").Get<MetricsSettings>() ?? new MetricsSettings();

        // Without a receiver no meter provider listens: the instruments record into nothing and no connection is opened.
        if (string.IsNullOrEmpty(settings.OtlpEndpoint))
        {
            return;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddMeter("ProfitFox.Bybit", "ProfitFox.Scanner")
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = new Uri(settings.OtlpEndpoint);
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = (int)settings.ExportInterval.TotalMilliseconds;
                }));
    }
}