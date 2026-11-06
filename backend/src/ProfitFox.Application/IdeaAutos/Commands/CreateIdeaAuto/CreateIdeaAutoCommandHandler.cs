using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Application.IdeaAutos.Commands.CreateIdeaAuto;

public sealed class CreateIdeaAutoCommandHandler(
    IIdeaAutoRepository autos,
    IExchangeAccountRepository accounts,
    IIdeaTradingSettingsRepository settings,
    TimeProvider time) : IRequestHandler<CreateIdeaAutoCommand, Guid>
{
    /// <exception cref="ValidationFailedException">A field is wrong.</exception>
    public async Task<Guid> Handle(CreateIdeaAutoCommand request, CancellationToken cancellationToken)
    {
        IdeaAutoConditions conditions = await IdeaAutoValidation.ParseAsync(request.Input, null, autos, accounts, settings, cancellationToken);
        IdeaAuto auto = IdeaAuto.Create(Guid.NewGuid(), request.Input.Name.Trim(), request.Input.AccountId, request.Input.Amount, conditions, 0) with { Budget = request.Input.Budget };
        await autos.CreateAsync(auto, time.GetUtcNow(), cancellationToken);

        return auto.Id;
    }
}