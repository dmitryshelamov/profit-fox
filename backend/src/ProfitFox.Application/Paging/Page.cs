namespace ProfitFox.Application.Paging;

/// <summary>One page of a list: its rows, its number from 1 (the one asked for, or the last page when that is past the end), its size and the count of all rows.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int Size, int Total);