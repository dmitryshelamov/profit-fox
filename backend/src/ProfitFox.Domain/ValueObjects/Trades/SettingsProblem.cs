namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     Why a setting of the risk module is refused (spec trade-risk «Настройки риска»): the field of the request it stands
///     under, a code and the reason.
/// </summary>
public sealed record SettingsProblem(string Field, string Code, string Reason);
