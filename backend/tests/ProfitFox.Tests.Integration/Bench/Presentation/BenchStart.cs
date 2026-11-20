namespace ProfitFox.Tests.Integration.Bench.Presentation;

/// <summary>
///     How a test starts the bench API: over which database, whether it migrates it at start, and from which folder it serves the
///     frontend.
/// </summary>
/// <param name="ConnectionString">The database; the migrated test database when null.</param>
/// <param name="Migrate">Whether to migrate the database at start, as <c>Program</c> does; the test database is migrated already.</param>
/// <param name="WebRoot">The built frontend; none when null.</param>
public sealed record BenchStart(string? ConnectionString = null, bool Migrate = false, string? WebRoot = null);
