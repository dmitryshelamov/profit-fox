using MediatR;

namespace ProfitFox.Application.SavedFilters.Commands.DeleteSavedFilter;

public sealed record DeleteSavedFilterCommand(Guid Id) : IRequest;