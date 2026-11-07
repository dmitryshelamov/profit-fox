using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos.Commands.SaveIdeaAuto;

public sealed class SaveIdeaAutoCommandHandler(IIdeaAutoRepository autos, IExchangeAccountRepository accounts, IIdeaTradingSettingsRepository settings) : IRequestHandler<SaveIdeaAutoCommand>
{
    /// <exception cref="NotFoundException">There is no such auto.</exception>
    /// <exception cref="ValidationFailedException">A field is wrong.</exception>
    public async Task Handle(SaveIdeaAutoCommand request, CancellationToken cancellationToken)
    {
        IdeaAuto auto = await autos.GetAsync(request.Id, cancellationToken) is { DeletedAt: null } found ? found : throw new NotFoundException($"There is no auto {request.Id}.");
        IdeaAutoConditions conditions = await IdeaAutoValidation.ParseAsync(request.Input, auto.Id, autos, accounts, settings, cancellationToken);
        await autos.SaveAsync(auto with { Name = request.Input.Name.Trim(), AccountId = request.Input.AccountId, Amount = request.Input.Amount, Budget = request.Input.Budget, Conditions = conditions }, cancellationToken);
    }
}