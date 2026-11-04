using MediatR;

namespace ProfitFox.Application.SavedFilters.Commands.CreateSavedFilter;

/// <summary>Saves a new filter; returns its id.</summary>
public sealed record CreateSavedFilterCommand(SavedFilterInput Filter) : IRequest<Guid>;