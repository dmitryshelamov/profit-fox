import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import type { RequestError } from '@/api/client';
import { setStrategyIdeas } from '@/api/ideas';
import type { StrategyListItem } from '@/api/strategies';
import { timeframes } from '@/preview/preview';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { Switch } from '@/shared/ui/Switch';
import { VersionBadge } from '@/shared/ui/VersionBadge';

/**
 * The ideas of a strategy in its row (spec strategy-ideas «Идеи в списке стратегий»): a strategy for a bot gives none;
 * a category of ideas has the switch and the badges of its timeframes, or «выключена». The badges open the window of the
 * switch and the five timeframes; turning on without a timeframe opens it too.
 */
export function StrategyIdeasCell({ strategy }: { readonly strategy: StrategyListItem }) {
  const [open, setOpen] = useState(false);
  const queries = useQueryClient();
  const enabled = strategy.ideas?.enabled === true;
  const chosen = strategy.ideas?.timeframes ?? [];
  // A refusal of the switch of the row opens the window with its reason (task 18.28, spec «Отказ в окне»).
  const [refused, setRefused] = useState<readonly RequestError[]>([]);

  if (strategy.kind === 'bot') {
    return <span className="text-xs text-faint">не даёт идей</span>;
  }

  const toggle = async (on: boolean) => {
    if (on && chosen.length === 0) {
      setOpen(true);
      return;
    }

    const result = await setStrategyIdeas(strategy.name, on, chosen);
    if (!result.ok) {
      setRefused(result.errors);
      setOpen(true);
    }

    await queries.invalidateQueries({ queryKey: ['strategies'] });
  };

  return (
    <span className="flex items-center gap-2">
      <Switch
        label={`Идеи ${strategy.name}`}
        checked={enabled}
        onChange={(on) => void toggle(on)}
      />
      <button
        type="button"
        aria-label={`Таймфреймы идей ${strategy.name}`}
        className="flex cursor-pointer items-center gap-1.5"
        onClick={() => setOpen(true)}
      >
        {enabled ? (
          chosen.map((timeframe) => (
            <Badge key={timeframe} mono>
              {timeframe}
            </Badge>
          ))
        ) : (
          <span className="text-xs text-muted hover:text-text">выключена</span>
        )}
      </button>
      {open && (
        <IdeasWindow
          strategy={strategy}
          refused={refused}
          onClose={() => {
            setOpen(false);
            setRefused([]);
          }}
        />
      )}
    </span>
  );
}

/** The window of the ideas of a strategy: each change is sent at once; a refusal stays in the window with its reason. */
function IdeasWindow({
  strategy,
  refused,
  onClose,
}: {
  readonly strategy: StrategyListItem;
  /** The refusal of the switch of the row that opened the window. */
  readonly refused: readonly RequestError[];
  readonly onClose: () => void;
}) {
  const queries = useQueryClient();
  const [enabled, setEnabled] = useState(strategy.ideas?.enabled === true);
  const [chosen, setChosen] = useState<readonly string[]>(strategy.ideas?.timeframes ?? []);
  const [errors, setErrors] = useState<readonly RequestError[]>(refused);

  const send = async (on: boolean, next: readonly string[]) => {
    const ordered = timeframes.filter((timeframe) => next.includes(timeframe));
    const before = chosen;
    setChosen(ordered);
    const result = await setStrategyIdeas(strategy.name, on, ordered);
    if (result.ok) {
      setEnabled(result.value.enabled);
      setErrors([]);
    } else {
      // A refused change goes back: the window shows what the bench keeps (task 18.28).
      setChosen(before);
      setErrors(result.errors);
    }

    await queries.invalidateQueries({ queryKey: ['strategies'] });
  };

  return (
    <Dialog label={`Идеи ${strategy.name}`} className="w-[420px]" onClose={onClose}>
      <span className="flex items-center gap-3">
        <h2 className="text-[15px] font-bold">Идеи · {strategy.name}</h2>
        <VersionBadge version={strategy.version} />
        <span className="ml-auto">
          <Switch
            label="Идеи включены"
            checked={enabled}
            onChange={(on) => void send(on, chosen)}
          />
        </span>
      </span>
      <span className="text-[11px] font-semibold text-muted uppercase">Таймфреймы</span>
      <span role="group" aria-label="Таймфреймы" className="grid grid-cols-5 gap-2">
        {timeframes.map((timeframe) => {
          const on = chosen.includes(timeframe);
          return (
            <button
              key={timeframe}
              type="button"
              aria-pressed={on}
              className={`h-8 cursor-pointer rounded-md border font-mono text-[12px] ${on ? 'border-accent bg-accent/15 text-text' : 'border-line text-muted hover:text-text'}`}
              onClick={() =>
                void send(
                  enabled,
                  on ? chosen.filter((item) => item !== timeframe) : [...chosen, timeframe],
                )
              }
            >
              {timeframe}
            </button>
          );
        })}
      </span>
      <p className="text-xs text-muted">
        Идеи считаются по последней сохранённой версии и её параметрам.
      </p>
      {errors.length > 0 && (
        <ul role="alert" aria-label="Отказ" className="flex flex-col gap-1 text-err">
          {errors.map((error) => (
            <li key={`${error.code}:${error.node ?? ''}:${error.field ?? ''}`}>{error.reason}</li>
          ))}
        </ul>
      )}
      <span className="flex justify-end">
        <Button onClick={onClose}>Закрыть</Button>
      </span>
    </Dialog>
  );
}
