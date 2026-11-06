namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The entries of an exit rule fired in a block of days and the total of their results in R.</summary>
public readonly record struct BlockSums(int Entries, decimal TotalR);