using MediatR;

using ProfitFox.Domain.Entities.Filters;
using ProfitFox.Domain.ValueObjects.Lists;

namespace ProfitFox.Application.Commands.Filters.SaveFilter;

/// <summary>
///     Saves a new filter, or the filter <see cref="Id" /> again (spec saved-filters «Сохранённый фильтр»).
/// </summary>
public sealed record SaveFilterCommand(Guid? Id, string? Name, string? Place, IReadOnlyList<FilterCondition>? Conditions) : IRequest<SavedFilter>;
