using ProfitFox.Domain.Entities.Backtests.Enums;

namespace ProfitFox.Application.Backtests;

/// <summary>
///     Codes of the backtest enums in the API and in stored rows: statuses by name (<c>Pending</c>, <c>LoadingCandles</c>…), limit fill
///     modes <c>touch</c> and <c>breakout</c>, the search mode <c>grid</c>.
/// </summary>
public static class BacktestCodes
{
    private static readonly Dictionary<string, LimitFillMode> ModesByCode = new Dictionary<string, LimitFillMode>
    {
        ["touch"] = LimitFillMode.Touch,
        ["breakout"] = LimitFillMode.Breakout
    };

    private static readonly Dictionary<string, SearchMode> SearchModesByCode = new Dictionary<string, SearchMode> { ["grid"] = SearchMode.Grid };

    private static readonly Dictionary<string, BacktestStatus> StatusesByCode = Enum.GetValues<BacktestStatus>().ToDictionary(status => status.ToString());

    public static bool TryParseMode(string code, out LimitFillMode mode)
    {
        return ModesByCode.TryGetValue(code, out mode);
    }

    public static bool TryParseSearchMode(string code, out SearchMode mode)
    {
        return SearchModesByCode.TryGetValue(code, out mode);
    }

    public static BacktestStatus ParseStatus(string code)
    {
        return StatusesByCode[code];
    }

    public static string ToCode(this LimitFillMode mode)
    {
        return ModesByCode.Single(pair => pair.Value == mode).Key;
    }

    public static string ToCode(this SearchMode mode)
    {
        return SearchModesByCode.Single(pair => pair.Value == mode).Key;
    }

    public static string ToCode(this BacktestStatus status)
    {
        return status.ToString();
    }
}