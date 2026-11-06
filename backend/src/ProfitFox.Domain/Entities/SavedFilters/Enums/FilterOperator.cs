namespace ProfitFox.Domain.Entities.SavedFilters.Enums;

/// <summary>How a condition compares its field: <c>&gt;</c>, <c>≥</c>, <c>&lt;</c>, <c>≤</c>, <c>=</c>, «между», or «надёжно > 0» for the badge of a bucket.</summary>
public enum FilterOperator
{
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Equal,
    Between,
    ReliablyPositive
}