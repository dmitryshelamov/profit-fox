namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas.Queries;

/// <summary>
///     The statements on <c>signal_labels</c>, a row per signal that has a label.
/// </summary>
internal static class SignalLabelRepositoryQueries
{
    public const string Find = """SELECT signal_id AS "SignalId", enabled AS "Enabled", text AS "Text", timeframe AS "Timeframe" FROM signal_labels WHERE signal_id = @SignalId""";

    public const string All = """SELECT signal_id AS "SignalId", enabled AS "Enabled", text AS "Text", timeframe AS "Timeframe" FROM signal_labels""";

    public const string Set =
        """
        INSERT INTO signal_labels (signal_id, enabled, text, timeframe) VALUES (@SignalId, @Enabled, @Text, @Timeframe)
        ON CONFLICT (signal_id) DO UPDATE SET enabled = EXCLUDED.enabled, text = EXCLUDED.text, timeframe = EXCLUDED.timeframe
        """;
}
