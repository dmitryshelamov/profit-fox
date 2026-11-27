namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring.Queries;

/// <summary>
///     The statements on <c>label_weights</c>, a row by the text of a label (change add-idea-score, design.md, decision 1).
/// </summary>
internal static class LabelWeightRepositoryQueries
{
    public const string All = """SELECT text AS "Text", long_weight AS "Long", short_weight AS "Short" FROM label_weights ORDER BY text""";

    public const string Set =
        """
        INSERT INTO label_weights (text, long_weight, short_weight) VALUES (@Text, @Long, @Short)
        ON CONFLICT (text) DO UPDATE SET long_weight = EXCLUDED.long_weight, short_weight = EXCLUDED.short_weight
        """;

    // The new text takes the weights of the old one when it has none of its own; the old text keeps its row only while a label
    // of a signal still has it (a label that is off may share the text of another one).
    public const string Move =
        """
        INSERT INTO label_weights (text, long_weight, short_weight)
        SELECT @To, long_weight, short_weight FROM label_weights WHERE text = @From
        ON CONFLICT (text) DO NOTHING;
        DELETE FROM label_weights WHERE text = @From AND NOT EXISTS (SELECT 1 FROM signal_labels WHERE text = @From);
        """;

    // A row a text: the label that is on, or the first by the name of its signal.
    public const string Labels =
        """
        SELECT l.text AS "Text", l.enabled AS "Enabled", l.timeframe AS "Timeframe", l.name AS "Signal",
               COALESCE(w.long_weight, 0)::int AS "LongWeight", COALESCE(w.short_weight, 0)::int AS "ShortWeight"
        FROM (SELECT DISTINCT ON (sl.text) sl.text, sl.enabled, sl.timeframe, s.name
              FROM signal_labels sl JOIN signals s ON s.id = sl.signal_id
              ORDER BY sl.text, sl.enabled DESC, s.name) l
        LEFT JOIN label_weights w ON w.text = l.text
        ORDER BY l.enabled DESC, l.text
        """;
}
