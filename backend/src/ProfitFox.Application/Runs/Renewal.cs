namespace ProfitFox.Application.Runs;

/// <summary>
///     The lease of a run was extended; <see cref="CancelRequested" /> tells that the owner asked to stop it.
/// </summary>
public sealed record Renewal(bool CancelRequested);
