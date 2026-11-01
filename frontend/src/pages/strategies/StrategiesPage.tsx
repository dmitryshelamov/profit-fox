import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { draftKey, drafts, newStrategyDrafts } from '@/modules/graph-editor';
import { directionTitles, strategiesQuery } from '@/modules/strategies';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';

import { StrategyActions } from './StrategyActions';

const cell = 'px-5 py-4';

export function StrategiesPage() {
  const strategies = useQuery(strategiesQuery);
  const navigate = useNavigate();
  const unsaved = newStrategyDrafts();

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Стратегии</h1>
          <p className="text-sm text-muted">
            Торговая логика без инструмента и интервала: их задают бот и бэктест.
          </p>
        </div>
        <Button
          look="primary"
          onClick={() =>
            void navigate({
              to: '/strategies/new/$draftId',
              params: { draftId: crypto.randomUUID() },
            })
          }
        >
          + Новая стратегия
        </Button>
      </header>
      {strategies.isError && <p role="alert">Не удалось загрузить стратегии.</p>}
      <table className="w-full border-separate border-spacing-y-2 text-sm">
        <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th className={cell}>Название</th>
            <th className={cell}>Направление</th>
            <th className={cell}>Категории рынка</th>
            <th className={cell} />
          </tr>
        </thead>
        <tbody>
          {unsaved.map((draft) => (
            <tr key={draft.draftId} className="bg-panel">
              <td className={cell}>
                <span className="flex items-center gap-3">
                  <Link
                    to="/strategies/new/$draftId"
                    params={{ draftId: draft.draftId }}
                    className="font-semibold hover:underline"
                  >
                    {draft.name || 'Без названия'}
                  </Link>
                  <Badge tone="warn" dot="var(--color-warn)">
                    не сохранена
                  </Badge>
                </span>
              </td>
              <td className={cell} />
              <td className={cell} />
              <td className={cell} />
            </tr>
          ))}
          {strategies.data?.map((strategy) => (
            <tr key={strategy.id} className="bg-panel">
              <td className={cell}>
                <span className="flex items-center gap-3">
                  <Link
                    to="/strategies/$id"
                    params={{ id: strategy.id }}
                    className="font-semibold hover:underline"
                  >
                    {strategy.name}
                  </Link>
                  {drafts.read(draftKey({ strategy: strategy.id })) && (
                    <Badge tone="warn" dot="var(--color-warn)">
                      черновик в браузере
                    </Badge>
                  )}
                </span>
              </td>
              <td className={cell}>
                <Badge>{directionTitles[strategy.direction] ?? strategy.direction}</Badge>
              </td>
              <td className={`${cell} font-mono text-muted`}>
                {strategy.supportedCategories.join(' · ')}
              </td>
              <td className={cell}>
                <StrategyActions id={strategy.id} name={strategy.name} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
