using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.IdeaAutos.Commands.ReorderIdeaAutos;

public sealed class ReorderIdeaAutosCommandHandler(IIdeaAutoRepository autos) : IRequestHandler<ReorderIdeaAutosCommand>
{
    /// <exception cref="ValidationFailedException">An auto is missing from the order, or an id is not that of an auto, or an id is there twice.</exception>
    public async Task Handle(ReorderIdeaAutosCommand request, CancellationToken cancellationToken)
    {
        var current = (await autos.GetAllAsync(cancellationToken)).Select(auto => auto.Id).ToHashSet();

        if (request.Ids.Count != current.Count || !current.SetEquals(request.Ids))
        {
            throw new ValidationFailedException(new Dictionary<string, string> { ["ids"] = "The order must hold every auto of the list once and no other id." });
        }

        await autos.ReorderAsync(request.Ids, cancellationToken);
    }
}