using System.Text.Json.Serialization;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A row of a table of a stored report of calibration with the statistics of its cases; decimals are strings.</summary>
[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
internal sealed class CalibrationRowDb
{
    public decimal? From { get; init; }

    public decimal? To { get; init; }

    public string? Badge { get; init; }

    public decimal Promised { get; init; }

    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int Targets { get; init; }

    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int Stops { get; init; }

    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int Expired { get; init; }

    public decimal? Probability { get; init; }

    public decimal? ProbabilityLow { get; init; }

    public decimal? ProbabilityHigh { get; init; }

    public decimal Expectancy { get; init; }

    public decimal? ExpectancyLow { get; init; }

    public decimal? ExpectancyHigh { get; init; }
}