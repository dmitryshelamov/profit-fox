namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A problem of a filter or of a list request and where it is: the field of the request — <c>name</c>, <c>place</c>,
///     <c>conditions</c>, <c>sort</c>, <c>pageSize</c> or a choice — or the number of the condition, from 0.
/// </summary>
public sealed record FilterProblem(string Code, string Reason)
{
    public string? Field { get; init; }

    public int? Condition { get; init; }
}
