using System.Text.Json.Serialization;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Contracts;

/// <summary>A row of the table by own data of a stored report of calibration; decimals are strings.</summary>
internal sealed class CalibrationOwnRowDb
{
    public bool FewOwnCases { get; init; }

    public required CalibrationRowDb Row { get; init; }

    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public decimal Brier { get; init; }
}