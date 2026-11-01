using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Commands.DeleteBot;

public sealed class DeleteBotCommandHandler(IStrategyLibraryRepository repository) : IRequestHandler<DeleteBotCommand>
{
    /// <exception cref="NotFoundException">There is no such bot.</exception>
    /// <exception cref="StateConflictException">The library changed while the bot was being deleted.</exception>
    public async Task Handle(DeleteBotCommand request, CancellationToken cancellationToken)
    {
        StrategyLibrary library = await repository.LoadAsync(cancellationToken);

        if (!library.RemoveBot(request.Id))
        {
            throw new NotFoundException($"There is no bot {request.Id}.");
        }

        StrategyRequests.EnsureSaved(await repository.SaveAsync(library.Change, library.Revision, cancellationToken));
    }
}