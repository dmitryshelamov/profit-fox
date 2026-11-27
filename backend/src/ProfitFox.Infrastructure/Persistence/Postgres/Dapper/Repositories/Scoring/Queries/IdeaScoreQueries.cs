using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Lists;

namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Scoring.Queries;

/// <summary>
///     The statements that keep <c>ideas.score</c> equal to <see cref="ListColumns.ScoreOf(string)" /> (change add-idea-score,
///     design.md, decision 2). A change of the weights holds the lock of the weights alone, a new idea holds it shared: new
///     ideas do not wait for each other, an idea begun during a change waits for its end and reads the new weights, and a change
///     waits for the ideas begun before it and counts them again. A save of many ideas by the scanner holds it shared too, so
///     it never interleaves with a change over the same rows. Each lock is a statement of its own, before the statements that
///     read the weights, so they read them once it is held.
/// </summary>
internal static class IdeaScoreQueries
{
    // The key of the lock of the weights among the advisory locks of the database: "scoring" in ASCII.
    private const long Key = 0x73636F72696E67;

    public static readonly string ShareWeights = $"SELECT pg_advisory_xact_lock_shared({Key})";

    public static readonly string LockWeights = $"SELECT pg_advisory_xact_lock({Key})";

    // The ideas that carry one of the texts, by the index of the labels; an idea whose score stays is not written.
    public static readonly string Recount =
        $"""
         UPDATE ideas AS i SET score = c.score
         FROM (SELECT o.id, {ListColumns.ScoreOf("o")} AS score FROM ideas o WHERE o.labels && @Texts::text[]) AS c
         WHERE i.id = c.id AND i.score <> c.score
         """;
}
