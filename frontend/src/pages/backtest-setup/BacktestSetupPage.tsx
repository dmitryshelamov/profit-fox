import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { BacktestForm } from '@/modules/backtests';
import { botQuery, type ParameterDef } from '@/modules/bots';
import { intervalLabel } from '@/modules/market-data';
import { strategiesQuery, strategyQuery } from '@/modules/strategies';

export function BacktestSetupPage({ botId }: { botId: string }) {
  // The period is counted from the moment the form was opened, so it does not move while the owner edits it.
  const [now] = useState(() => Date.now());
  const bot = useQuery(botQuery(botId));
  const strategyId = bot.data?.strategyId ?? '';
  const strategies = useQuery(strategiesQuery);
  const strategy = useQuery({ ...strategyQuery(strategyId), enabled: strategyId !== '' });
  const declared = ((strategy.data?.definition as { parameters?: ParameterDef[] } | undefined)
    ?.parameters ?? []) as ParameterDef[];
  const strategyName = strategies.data?.find((item) => item.id === strategyId)?.name ?? '';

  return (
    <section className="space-y-5">
      <header className="space-y-2">
        <p className="text-xs text-muted">
          <Link to="/bots" className="hover:underline">
            Боты
          </Link>{' '}
          /{' '}
          <Link to="/bots/$botId" params={{ botId }} className="hover:underline">
            {bot.data?.name}
          </Link>{' '}
          / Новый бэктест
        </p>
        <h1 className="text-2xl font-bold">Новый бэктест</h1>
        {bot.data && strategy.data && (
          <p className="text-sm text-muted">
            {strategyName} · {bot.data.category} · {bot.data.symbol} ·{' '}
            {intervalLabel(bot.data.interval)} · последняя версия стратегии v
            {String(strategy.data.version)}
          </p>
        )}
      </header>
      {bot.isError && <p role="alert">Не удалось загрузить бота.</p>}
      {bot.data && strategy.data && (
        <BacktestForm botId={botId} bot={bot.data} declared={declared} now={now} />
      )}
    </section>
  );
}
