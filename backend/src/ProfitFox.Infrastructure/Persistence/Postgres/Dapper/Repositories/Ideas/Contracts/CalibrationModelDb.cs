using System.Text.Json.Serialization;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A model of the comparison of a stored report of calibration; decimals are strings.</summary>
internal sealed class CalibrationModelDb
{
    public int Cases { get; init; }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal? Brier { get; init; }

    public CalibrationRowDb? Up { get; init; }
}