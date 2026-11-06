using MediatR;

using ProfitFox.Application.Backtests;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Ideas.Queries.GetExitRulePreview;

/// <summary>Reads the signals of the type × interval fired in the last 90 days at once and previews the candidate on them in memory.</summary>
public sealed class GetExitRulePreviewQueryHandler(
    IIdeaSignalRepository signals,
    IIdeaExitRuleRepository exitRules,
    BacktestSettings backtests,
    IdeasEngineVersion version,
    TimeProvider time) : IRequestHandler<GetExitRulePreviewQuery, ExitRulePreview>
{
    private static readonly TimeSpan Read = TimeSpan.FromDays(90);

    /// <exception cref="Exceptions.ValidationFailedException">An unknown type or interval, or a value the rule cannot take.</exception>
    public async Task<ExitRulePreview> Handle(GetExitRulePreviewQuery request, CancellationToken cancellationToken)
    {
        IdeaExitRule candidate = ExitRuleRequests.Parse(request.Type, request.Interval, request.Life, request.Target, request.Window);
        IdeaExitRule current = new IdeaExitRules(await exitRules.GetAllAsync(cancellationToken)).Of(candidate.Type, candidate.Interval);
        DateTimeOffset now = time.GetUtcNow();
        IdeaSignal[] read = await signals.StreamAsync(version.Number, now - Read, (candidate.Type, candidate.Interval), cancellationToken).ToArrayAsync(cancellationToken);
        IReadOnlyDictionary<MarketCategory, FeeRates> fees = backtests.Policy().DefaultFees;

        return ExitRulePreviews.Compute(read, current, candidate, category => fees[category], now);
    }
}