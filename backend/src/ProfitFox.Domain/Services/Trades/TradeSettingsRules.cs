using System.Globalization;

using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The rules of the settings of the risk module (change take-idea-trades, design.md, decisions 8 and 9; spec trade-risk
///     «Настройки риска», «Сроки и попытки», «Стоп ближе ликвидации», «Без пределов входа нет»). Every refused field is told
///     at once, each at its field, in the order of the page.
/// </summary>
public static class TradeSettingsRules
{
    public const int MaxLeverage = 20;

    public static readonly TimeSpan MinExpiryMargin = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxExpiryMargin = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan MinEntryWait = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxEntryWait = TimeSpan.FromSeconds(120);
    public const int MinStopAttempts = 1;
    public const int MaxStopAttempts = 10;

    /// <summary>
    ///     Every problem of the settings; empty when they can be saved. A limit may be left empty: the entry then is refused,
    ///     not the save.
    /// </summary>
    public static IReadOnlyList<SettingsProblem> Check(TradeSettings settings)
    {
        var problems = new List<SettingsProblem>();
        if (settings.DefaultAmount is { } amount)
        {
            if (amount <= 0)
            {
                problems.Add(new SettingsProblem("defaultAmount", "Positive", "сумма по умолчанию — больше нуля"));
            }
            else if (settings.MaxAmount is { } max && amount > max)
            {
                problems.Add(new SettingsProblem("defaultAmount", "AboveLimit", $"сумма по умолчанию больше предела на сделку {Number(max)} USDT"));
            }
        }

        if (settings.Leverage is { } leverage && leverage is < 1 or > MaxLeverage)
        {
            problems.Add(new SettingsProblem("leverage", "Range", $"плечо — целое от 1 до {MaxLeverage}"));
        }

        if (settings.MaxAmount is <= 0)
        {
            problems.Add(new SettingsProblem("maxAmount", "Positive", "предел на сделку — больше нуля"));
        }

        if (settings.MaxOpenTrades is { } open && open < 1)
        {
            problems.Add(new SettingsProblem("maxOpenTrades", "Positive", "число открытых сделок — целое от 1"));
        }

        if (settings.MaxTotalAmount is <= 0)
        {
            problems.Add(new SettingsProblem("maxTotalAmount", "Positive", "общая сумма — больше нуля"));
        }

        if (settings.MaxLossPercent is { } loss)
        {
            if (loss <= 0)
            {
                problems.Add(new SettingsProblem("maxLossPercent", "Positive", "предел потери — больше нуля"));
            }
            else if (settings.Leverage is { } lever and >= 1 and <= MaxLeverage && loss >= HalfWayToLiquidation(lever))
            {
                problems.Add(new SettingsProblem("maxLossPercent", "TooWide", $"при плече {lever}× предел потери должен быть меньше {HalfWayText(lever)} %"));
            }
        }

        if (settings.MaxDayLoss is <= 0)
        {
            problems.Add(new SettingsProblem("maxDayLoss", "Positive", "убыток за день — больше нуля"));
        }

        if (settings.ExpiryMargin < MinExpiryMargin || settings.ExpiryMargin > MaxExpiryMargin || settings.ExpiryMargin.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            problems.Add(new SettingsProblem("expiryMarginMinutes", "Range", "запас до срока — от 1 до 60 минут"));
        }

        if (settings.EntryWait < MinEntryWait || settings.EntryWait > MaxEntryWait || settings.EntryWait.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            problems.Add(new SettingsProblem("entryWaitSeconds", "Range", "ждать приёма входа — от 10 до 120 секунд"));
        }

        if (settings.StopAttempts is < MinStopAttempts or > MaxStopAttempts)
        {
            problems.Add(new SettingsProblem("stopAttempts", "Range", "попыток вернуть стоп — от 1 до 10"));
        }

        return problems;
    }

    /// <summary>
    ///     The limits not set, by the names of the page, in its order: the leverage, the amount of a trade, the number of
    ///     open trades, the amount in all, the loss of a trade and the loss of a day. Empty when a trade may be entered.
    /// </summary>
    public static IReadOnlyList<string> Missing(TradeSettings settings)
    {
        var missing = new List<string>();
        if (settings.Leverage is null)
        {
            missing.Add("плечо");
        }

        if (settings.MaxAmount is null)
        {
            missing.Add("сумма на сделку");
        }

        if (settings.MaxOpenTrades is null)
        {
            missing.Add("число открытых сделок");
        }

        if (settings.MaxTotalAmount is null)
        {
            missing.Add("общая сумма в сделках");
        }

        if (settings.MaxLossPercent is null)
        {
            missing.Add("потеря на сделку");
        }

        if (settings.MaxDayLoss is null)
        {
            missing.Add("убыток за день");
        }

        return missing;
    }

    /// <summary>
    ///     Half of the way to the liquidation in percent at the leverage: <c>100 / (2 × leverage)</c> (design.md, decision 9).
    /// </summary>
    public static decimal HalfWayToLiquidation(int leverage)
    {
        return 100m / (2 * leverage);
    }

    /// <summary>
    ///     Half of the way to the liquidation as the page writes it: cut, not rounded, to a tenth, so that a number below it
    ///     is allowed — <c>16.6</c> at 3×, <c>5</c> at 10×.
    /// </summary>
    public static string HalfWayText(int leverage)
    {
        return (decimal.Truncate(HalfWayToLiquidation(leverage) * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string Number(decimal value)
    {
        return decimal.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
