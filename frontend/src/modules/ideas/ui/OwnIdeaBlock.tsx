import { useQuery } from '@tanstack/react-query';

import { setupIntervals } from '@/modules/market';
import { shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal, showDecimal } from '@/shared/format/format';

import { type OwnIdeaInput, ownIdeaQuery } from '../api';
import { ideaTypes, sideLabel } from '../model/labels';
import { priceByTick } from '../model/price';

const field = 'h-8 rounded-md border border-line bg-panel-2 px-2 text-sm';

/** The levels of an own idea for the choice the block holds; the page draws them on the chart. */
export function useOwnIdea(category: string, symbol: string, input: OwnIdeaInput) {
  return useQuery({
    ...ownIdeaQuery(category, symbol, input),
    // There is no short on spot: the choice is not asked.
    enabled: !(category === 'spot' && input.side === 'short'),
  });
}

/**
 * «Своя идея»: the owner picks a type, an interval and a side, and sees the entry, stop, target, R:R and the baseline, the
 * only probability an idea without a setup has. a short is not offered on spot.
 */
export function OwnIdeaBlock({
  category,
  symbol,
  input,
  onChange,
}: {
  category: string;
  symbol: string;
  input: OwnIdeaInput;
  onChange: (input: OwnIdeaInput) => void;
}) {
  const own = useOwnIdea(category, symbol, input);
  const spot = category === 'spot';
  const result = own.data;

  return (
    <section
      aria-label="Своя идея"
      className="space-y-3 rounded-xl border border-line bg-panel p-4"
    >
      <h2 className="text-sm font-semibold">Своя идея</h2>
      <div className="flex flex-wrap gap-2">
        <select
          aria-label="Тип"
          value={input.type}
          onChange={(event) => onChange({ ...input, type: event.target.value })}
          className={field}
        >
          {ideaTypes.map((type) => (
            <option key={type.value} value={type.value}>
              {type.label}
            </option>
          ))}
        </select>
        <select
          aria-label="ТФ"
          value={input.interval}
          onChange={(event) => onChange({ ...input, interval: event.target.value })}
          className={field}
        >
          {setupIntervals.map((interval) => (
            <option key={interval.value} value={interval.value}>
              {interval.label}
            </option>
          ))}
        </select>
        <div role="group" aria-label="Сторона" className="flex gap-1">
          {(['long', 'short'] as const).map((side) => (
            <button
              key={side}
              type="button"
              aria-pressed={input.side === side}
              disabled={spot && side === 'short'}
              onClick={() => onChange({ ...input, side })}
              className="h-8 rounded-md px-3 text-sm text-muted hover:bg-panel-2 disabled:opacity-40 aria-pressed:bg-panel-2 aria-pressed:text-text"
            >
              {sideLabel(side)}
            </button>
          ))}
        </div>
      </div>
      {spot && <p className="text-xs text-muted">на spot шорта нет</p>}
      {own.isError && <p role="alert">Не удалось посчитать идею.</p>}
      {own.isPending && !own.isError && <p className="text-sm text-muted">Расчёт…</p>}
      {result && !result.made && (
        <p className="text-sm text-muted">
          Идеи нет: правила не дают её на этой паре. Нет подходящей зоны, R:R меньше 1.5 или мало
          истории.
        </p>
      )}
      {result?.made && (
        <dl className="grid grid-cols-2 gap-2 font-mono text-sm">
          <Level label="Вход" value={priceByTick(result.entry!, result.tick)} />
          <Level label="Стоп" value={priceByTick(result.stop!, result.tick)} />
          <Level label="Цель" value={priceByTick(result.target!, result.tick)} />
          <Level label="R:R" value={showDecimal(result.riskReward!, 2)} />
          <Level
            label="Базовая линия"
            value={`${roundDecimal(shiftDecimal(result.baseline!, 2), 0)} %`}
          />
        </dl>
      )}
      {result?.made && <p className="text-xs text-faint">без сетапа: только базовая линия</p>}
    </section>
  );
}

function Level({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[11px] text-muted">{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
