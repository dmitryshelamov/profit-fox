using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The count of an entry and its checks (change take-idea-trades, design.md, decisions 7, 8 and 9): one pure function the
///     dialog and the entry both call with the data read anew. The quantity is the amount over the price now, down to the
///     step of the pair; the stop is the protective one; the levels are those of the idea, whenever the click; the loss and
///     the profit are estimated at the taker fee of <see cref="TradeCosts.Default" /> on both sides. The checks go in the
///     order of the dialog, and every one is told, passed or not.
/// </summary>
public static class EntryCalculator
{
    /// <summary>
    ///     The margin of maintenance the price of the liquidation is estimated with: that of the first tier of Bybit.
    /// </summary>
    public const decimal MaintenanceMargin = 0.005m;

    public static EntryCalculation Count(EntryInput input)
    {
        Idea idea = input.Idea;
        TradeSettings settings = input.Settings;
        TradeSide side = idea.Side == EntrySide.Long ? TradeSide.Long : TradeSide.Short;
        int sign = (int)side;
        decimal price = input.Price;
        int leverage = settings.Leverage ?? 1;
        decimal fee = (decimal)TradeCosts.Default.TakerFee;
        PairRules? rules = input.Rules;

        (decimal entry, decimal? ideaStop, decimal? ideaTarget) = Levels(idea);
        decimal step = rules?.QuantityStep ?? 0m;
        decimal quantity = price > 0 ? input.Amount / price : 0m;
        quantity = step > 0 ? decimal.Floor(quantity / step) * step : quantity;
        decimal value = quantity * price;
        decimal margin = value / leverage;
        decimal tick = rules?.TickSize ?? 0m;

        decimal? stop = null, limitStop = null;
        StopSource? source = null;
        // A pair the exchange gives no price for: nothing is counted from the price, and the checks say so.
        bool priced = price > 0;
        if (priced && settings.MaxLossPercent is { } maxLoss)
        {
            (decimal stopPrice, StopSource stopSource, decimal limit) = ProtectiveStop.Of(side, price, ideaStop, maxLoss, tick);
            (stop, source, limitStop) = (stopPrice, stopSource, limit);
        }
        else if (priced && ideaStop is { } onlyIdea)
        {
            (stop, source) = (ProtectiveStop.ToStep(onlyIdea, tick, MidpointRounding.AwayFromZero), StopSource.Idea);
        }

        decimal? target = ideaTarget is { } goal ? ProtectiveStop.ToStep(goal, tick, MidpointRounding.AwayFromZero) : null;
        decimal? lossAtStop = stop is { } s ? quantity * (s - price) * sign - value * fee - quantity * s * fee : null;
        decimal? profitAtTarget = target is { } t ? quantity * (t - price) * sign - value * fee - quantity * t * fee : null;
        decimal liquidation = priced ? price * (1 - sign * (1m / leverage - MaintenanceMargin)) : 0m;
        decimal needed = margin + 2 * value * fee;

        var checks = new List<EntryCheck>
        {
            Limits(settings),
            Check("halt", !input.Counters.Halted, "Стоп-кран не сработал", "Вход запрещён стоп-краном"),
            Check("trader", input.Counters.TraderAlive, "Торговый Worker работает", "Торговый Worker не работает"),
            Check("idea", idea.State == IdeaState.Open, "Идея открыта", "Идея закрыта"),
            TimeLeft(idea, settings, input.Now),
            priced ? PriceLevels(side, price, ideaStop, ideaTarget) : new EntryCheck("levels", false, "Цена пары неизвестна"),
            !priced ? new EntryCheck("pair", false, $"Цена {idea.Symbol} неизвестна: Bybit её не дал") : Check("pair", rules is { Trading: true }, $"{idea.Symbol} торгуется", $"{idea.Symbol} не торгуется на Bybit")
        };
        checks.AddRange(AccountChecks(input, idea.Symbol));
        checks.Add(AmountLimit(settings, input.Amount));
        checks.Add(OpenCount(settings, input.Counters));
        checks.Add(TotalAmount(settings, input.Counters, input.Amount));
        checks.Add(Check(
            "minimum",
            rules is not null && quantity > 0 && quantity >= rules.MinQuantity && value >= rules.MinNotional,
            "Количество и стоимость не меньше минимума",
            "Сумма меньше минимума биржи для пары"));
        checks.Add(rules is null
            ? new EntryCheck("leverage", false, "Плечо пары неизвестно")
            : Check("leverage", leverage <= rules.MaxLeverage, $"Плечо {leverage}× не больше максимума пары", $"Плечо {leverage}× больше максимума пары {TradeTexts.Price(rules.MaxLeverage)}×"));
        checks.Add(stop is { } guard && priced
            ? Check(
                "liquidation",
                Math.Abs(price - guard) / price < 1m / (2 * leverage),
                $"Стоп ближе ликвидации (≈ {Approximate(liquidation, tick)})",
                $"Стоп дальше половины пути до ликвидации (≈ {Approximate(liquidation, tick)})")
            : new EntryCheck("liquidation", false, "Стопа нет: задайте предел потери на сделку"));
        checks.Add(input.Account?.Free is { } free
            ? Check("free", free >= needed, "Свободных USDT хватает", $"Свободных USDT не хватает: {TradeTexts.Money(decimal.Round(free, 2))} из {TradeTexts.Money(decimal.Round(needed, 2))}")
            : new EntryCheck("free", false, "Свободные USDT не прочитаны"));

        return new EntryCalculation(
            side,
            price,
            input.Amount,
            leverage,
            quantity,
            value,
            margin,
            entry,
            ideaStop,
            ideaTarget,
            stop,
            source,
            limitStop,
            stop is { } d && priced ? (d - price) / price : null,
            target,
            target is { } g && priced ? (g - price) / price : null,
            lossAtStop,
            profitAtTarget,
            liquidation,
            needed,
            idea.Until,
            checks);
    }

    /// <summary>
    ///     The entry, stop and target of the idea in prices: from its entry, or from the close of its candle before it.
    /// </summary>
    public static (decimal Entry, decimal? Stop, decimal? Target) Levels(Idea idea)
    {
        double entry = idea.ShownEntry;
        double direction = idea.Side == EntrySide.Long ? 1 : -1;
        return ((decimal)entry,
            idea.Stop is { } stop ? (decimal)(entry * (1 - direction * stop)) : null,
            idea.Target is { } target ? (decimal)(entry * (1 + direction * target)) : null);
    }

    private static EntryCheck Limits(TradeSettings settings)
    {
        IReadOnlyList<string> missing = TradeSettingsRules.Missing(settings);
        return missing.Count == 0
            ? new EntryCheck("limits", true, "Пределы заданы")
            : new EntryCheck("limits", false, $"Задайте пределы в разделе «Риск»: {string.Join(", ", missing)}");
    }

    private static EntryCheck TimeLeft(Idea idea, TradeSettings settings, DateTimeOffset now)
    {
        int minutes = (int)Math.Round(settings.ExpiryMargin.TotalMinutes);
        return Check("time", idea.Until - now >= settings.ExpiryMargin, "До срока идеи больше запаса", $"До срока идеи {TradeTexts.LessThanMinutes(minutes)}");
    }

    private static EntryCheck PriceLevels(TradeSide side, decimal price, decimal? stop, decimal? target)
    {
        string now = TradeTexts.Price(price);
        if (side == TradeSide.Long)
        {
            if (stop is { } low && price <= low)
            {
                return new EntryCheck("levels", false, $"Цена {now} уже ниже стопа идеи {TradeTexts.Price(decimal.Round(low, 8))}");
            }

            if (target is { } high && price >= high)
            {
                return new EntryCheck("levels", false, $"Цена {now} уже выше цели идеи {TradeTexts.Price(decimal.Round(high, 8))}");
            }
        }
        else
        {
            if (stop is { } high && price >= high)
            {
                return new EntryCheck("levels", false, $"Цена {now} уже выше стопа идеи {TradeTexts.Price(decimal.Round(high, 8))}");
            }

            if (target is { } low && price <= low)
            {
                return new EntryCheck("levels", false, $"Цена {now} уже ниже цели идеи {TradeTexts.Price(decimal.Round(low, 8))}");
            }
        }

        return new EntryCheck("levels", true, "Цена не прошла стоп и цель идеи");
    }

    private static IEnumerable<EntryCheck> AccountChecks(EntryInput input, string symbol)
    {
        EntryAccount? account = input.Account;
        if (account is null)
        {
            yield return new EntryCheck("key", false, "Аккаунт не выбран: добавьте аккаунт в «Аккаунтах»");
            yield return new EntryCheck("margin", false, "Режим маржи не прочитан");
            yield return new EntryCheck("oneWay", false, "Режим позиции не прочитан");
            yield return Check("pairTrade", !input.Counters.PairTaken, $"По {symbol} сделок нет", "По паре уже есть сделка");
            yield return new EntryCheck("position", false, "Позиции аккаунта не прочитаны");
            yield break;
        }

        yield return account.KeyProblem is { } problem ? new EntryCheck("key", false, problem) : new EntryCheck("key", true, "Ключ аккаунта действует и без права вывода");
        yield return account.KeyProblem is not null
            ? new EntryCheck("margin", false, "Режим маржи не прочитан: ключ не принят")
            : account.MarginMode switch
            {
                "ISOLATED_MARGIN" => new EntryCheck("margin", true, "Аккаунт в режиме изолированной маржи"),
                "REGULAR_MARGIN" => new EntryCheck("margin", false, "Аккаунт в режиме кросс-маржи, переключите его на Bybit на изолированную маржу"),
                "PORTFOLIO_MARGIN" => new EntryCheck("margin", false, "Аккаунт в режиме портфельной маржи, переключите его на Bybit на изолированную маржу"),
                null => new EntryCheck("margin", false, "Bybit не назвал режим маржи аккаунта"),
                { } other => new EntryCheck("margin", false, $"Режим маржи аккаунта {other} стенд не знает")
            };

        IReadOnlyList<ExchangePosition> positions = [.. account.Positions.Where(position => position.Symbol == symbol)];
        yield return account.KeyProblem is not null
            ? new EntryCheck("oneWay", false, "Режим позиции не прочитан: ключ не принят")
            : Check("oneWay", positions.All(position => position.OneWay), "Пара в одностороннем режиме", "Пара в режиме хеджа, переключите её на Bybit в односторонний режим");
        yield return Check("pairTrade", !input.Counters.PairTaken, $"По {symbol} сделок нет", "По паре уже есть сделка");
        yield return account.KeyProblem is not null
            ? new EntryCheck("position", false, "Позиции не прочитаны: ключ не принят")
            : Check("position", input.Counters.PairTaken || positions.All(position => position.Size == 0), $"По {symbol} позиций нет", "По паре на аккаунте есть позиция не из стенда");
    }

    private static EntryCheck AmountLimit(TradeSettings settings, decimal amount)
    {
        return settings.MaxAmount is { } max
            ? Check("amount", amount <= max, $"Сумма не больше {TradeTexts.Money(max)} USDT", $"Сумма больше предела {TradeTexts.Money(max)} USDT")
            : new EntryCheck("amount", false, "Предел суммы на сделку не задан");
    }

    private static EntryCheck OpenCount(TradeSettings settings, EntryCounters counters)
    {
        return settings.MaxOpenTrades is { } max
            ? Check("open", counters.OpenTrades < max, $"Открытых сделок {counters.OpenTrades} из {max}", $"Открыто {counters.OpenTrades} сделок из {max}")
            : new EntryCheck("open", false, "Предел числа открытых сделок не задан");
    }

    private static EntryCheck TotalAmount(TradeSettings settings, EntryCounters counters, decimal amount)
    {
        if (settings.MaxTotalAmount is not { } max)
        {
            return new EntryCheck("total", false, "Предел общей суммы не задан");
        }

        decimal after = counters.OpenAmount + amount;
        return Check("total", after <= max, $"В сделках {TradeTexts.Money(counters.OpenAmount)} из {TradeTexts.Money(max)} USDT", $"В сделках было бы {TradeTexts.Money(after)} USDT при пределе {TradeTexts.Money(max)}");
    }

    private static string Approximate(decimal price, decimal tick)
    {
        decimal step = tick > 0 ? tick * 10 : 0.01m;
        return TradeTexts.Price(ProtectiveStop.ToStep(price, step, MidpointRounding.AwayFromZero));
    }

    private static EntryCheck Check(string code, bool passed, string ok, string refused)
    {
        return new EntryCheck(code, passed, passed ? ok : refused);
    }
}
