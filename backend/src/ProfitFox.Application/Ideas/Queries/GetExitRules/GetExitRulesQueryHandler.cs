using MediatR;

using ProfitFox.Application.Scanner.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetExitRules;

public sealed class GetExitRulesQueryHandler(IIdeaExitRuleRepository exitRules) : IRequestHandler<GetExitRulesQuery, GetExitRulesResult>
{
    public async Task<GetExitRulesResult> Handle(GetExitRulesQuery request, CancellationToken cancellationToken)
    {
        var rules = new IdeaExitRules(await exitRules.GetAllAsync(cancellationToken));

        return new GetExitRulesResult(
            [.. Enum.GetValues<IdeaType>().SelectMany(type => IdeasSettings.Intervals.Select(interval => rules.Of(type, interval)))],
            IdeasSettings.Intervals.ToDictionary(interval => interval, ExitRuleGrid.Of));
    }
}