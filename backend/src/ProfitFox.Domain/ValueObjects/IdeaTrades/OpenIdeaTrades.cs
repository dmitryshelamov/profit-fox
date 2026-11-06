namespace ProfitFox.Domain.ValueObjects.IdeaTrades;

/// <summary>The trades by ideas that wait for their entry or are open, of every account: how many, and the risk they were entered with.</summary>
public sealed record OpenIdeaTrades(int Count, decimal Risk);