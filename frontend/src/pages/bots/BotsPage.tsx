import { useQueries, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { StatusBadge } from '@/modules/backtests';
import { botsQuery } from '@/modules/bots';
import { instrumentsQuery, intervalLabel } from '@/modules/market-data';
import { strategiesQuery } from '@/modules/strategies';
import { combinationsWord, groupDigits } from '@/shared/format/format';
import { Badge } from '@/shared/ui/Badge';

const cell = 'px-5 py-4';

export function BotsPage() {
  const bots = useQuery(botsQuery);
  const strategies = useQuery(strategiesQuery);
  const pairs = [
    ...new Map(
      (bots.data ?? []).map(({ category, symbol }) => [
        `${category} ${symbol}`,
        { category, symbol },
      ]),
    ).values(),
  ];
  const instruments = useQueries({
    queries: pairs.map(({ category, symbol }) => instrumentsQuery(category, symbol)),
  });
  const quoteCoinOf = (category: string, symbol: string) =>
    instruments[
      pairs.findIndex((pair) => pair.category === category && pair.symbol === symbol)
    ]?.data?.find((item) => item.symbol === symbol)?.quoteCoin;
  const balanceOf = ({
    balance,
    category,
    symbol,
  }: {
    balance: string;
    category: string;
    symbol: string;
  }) => {
    const quoteCoin = quoteCoinOf(category, symbol);

    return quoteCoin ? `${groupDigits(balance)} ${quoteCoin}` : groupDigits(balance);
  };
  const strategyName = (id: string) => strategies.data?.find((item) => item.id === id)?.name ?? '';

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Боты</h1>
          <p className="text-sm text-muted">
            Стратегия, настроенная для торговли: значения параметров, инструмент, интервал и баланс.
            Бота проверяют бэктестами.
          </p>
        </div>
        <Link
          to="/bots/new"
          search={{ strategyId: undefined }}
          className="inline-flex h-8 items-center rounded-md bg-accent px-3 text-sm font-bold text-on-accent hover:brightness-110"
        >
          + Новый бот
        </Link>
      </header>
      {bots.isError && <p role="alert">Не удалось загрузить ботов.</p>}
      <table className="w-full border-separate border-spacing-y-2 text-sm">
        <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th className={cell}>Название</th>
            <th className={cell}>Стратегия</th>
            <th className={cell}>Инструмент</th>
            <th className={cell}>Интервал</th>
            <th className={cell}>Баланс</th>
            <th className={cell}>Последний бэктест</th>
            <th className={cell} />
          </tr>
        </thead>
        <tbody>
          {bots.data?.map((bot) => (
            <tr key={bot.id} className="bg-panel">
              <td className={`${cell} font-semibold`}>{bot.name}</td>
              <td className={cell}>{strategyName(bot.strategyId)}</td>
              <td className={`${cell} font-mono whitespace-nowrap text-muted`}>
                {bot.category} · {bot.symbol}
              </td>
              <td className={cell}>{intervalLabel(bot.interval)}</td>
              <td className={`${cell} font-mono whitespace-nowrap`}>{balanceOf(bot)}</td>
              <td className={`${cell} whitespace-nowrap`}>
                {bot.lastBacktest ? (
                  <span className="flex items-center gap-2">
                    <StatusBadge
                      status={bot.lastBacktest.status}
                      progress={bot.lastBacktest.progress}
                    />
                    {bot.lastBacktest.status === 'Succeeded' && (
                      <span className="text-muted">
                        {groupDigits(String(bot.lastBacktest.progress.totalCombinations))}{' '}
                        {combinationsWord(bot.lastBacktest.progress.totalCombinations)}
                      </span>
                    )}
                  </span>
                ) : (
                  <Badge>Нет бэктестов</Badge>
                )}
              </td>
              <td className={`${cell} text-right whitespace-nowrap`}>
                <Link
                  to="/bots/$botId"
                  params={{ botId: bot.id }}
                  className="font-semibold text-accent hover:underline"
                  aria-label={`Открыть: ${bot.name}`}
                >
                  Открыть →
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="text-xs text-faint">
        Бот создаётся здесь или действием «Создать бота» в списке стратегий. Стратегию бота поменять
        нельзя — только создать нового.
      </p>
    </section>
  );
}
