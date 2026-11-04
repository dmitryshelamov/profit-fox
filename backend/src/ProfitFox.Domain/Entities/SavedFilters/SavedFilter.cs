using ProfitFox.Domain.Entities.SavedFilters.Enums;
using ProfitFox.Domain.ValueObjects.SavedFilters;

namespace ProfitFox.Domain.Entities.SavedFilters;

/// <summary>A named set of conditions joined by «и», shown on «Рынок», «Идеи» or both; the browser applies it.</summary>
public sealed record SavedFilter(Guid Id, string Name, SavedFilterPlace Place, IReadOnlyList<FilterCondition> Conditions, DateTimeOffset CreatedAt);