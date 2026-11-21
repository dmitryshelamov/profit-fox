namespace ProfitFox.Presentation.Signals;

/// <summary>
///     The new name of a signal.
/// </summary>
public sealed record RenameSignalRequest(string? To);
