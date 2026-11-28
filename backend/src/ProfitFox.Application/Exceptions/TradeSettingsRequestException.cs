using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     The settings of the risk module were refused with every problem, each at its field (spec trade-risk «Настройки
///     риска»); nothing was written.
/// </summary>
public sealed class TradeSettingsRequestException(IReadOnlyList<SettingsProblem> problems)
    : Exception(string.Join("; ", problems.Select(problem => problem.Reason)))
{
    public IReadOnlyList<SettingsProblem> Problems { get; } = problems;
}
