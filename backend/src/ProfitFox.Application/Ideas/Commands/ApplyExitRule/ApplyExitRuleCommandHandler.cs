using MediatR;

using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Commands.ApplyExitRule;

public sealed class ApplyExitRuleCommandHandler(IIdeaExitRuleRepository exitRules, TimeProvider time) : IRequestHandler<ApplyExitRuleCommand, IdeaExitRule>
{
    /// <exception cref="Exceptions.ValidationFailedException">An unknown type or interval, or a value the rule cannot take.</exception>
    public async Task<IdeaExitRule> Handle(ApplyExitRuleCommand request, CancellationToken cancellationToken)
    {
        IdeaExitRule rule = ExitRuleRequests.Parse(request.Type, request.Interval, request.Life, request.Target, request.Window) with { AppliedAt = time.GetUtcNow() };
        await exitRules.SaveAsync(rule, cancellationToken);

        return rule;
    }
}