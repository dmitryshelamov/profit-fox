namespace ProfitFox.Tests.Integration.Presentation;

/// <summary>
///     How a test starts the bench API: over which database, and from which folder it serves the frontend.
/// </summary>
/// <param name="ConnectionString">The database; the migrated test database when null.</param>
/// <param name="WebRoot">The built frontend; none when null.</param>
public sealed record BenchStart(string? ConnectionString = null, string? WebRoot = null);
