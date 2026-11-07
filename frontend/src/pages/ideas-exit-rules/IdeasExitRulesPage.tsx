import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import {
  badgeSymbol,
  candlesOfHours,
  candlesText,
  counted,
  durationText,
  type ExitRuleCandidate,
  exitRuleOutOfSampleQuery,
  exitRulePreviewQuery,
  exitRulesQuery,
  ideaTypes,
  LifeChart,
  MfeChart,
  overfittingText,
  overfitWarning,
  sameRule,
  typeLabel,
  useApplyExitRule,
} from '@/modules/ideas';
import type { Schemas } from '@/shared/api/client';
import { shiftDecimal } from '@/shared/format/decimal';
import { formatMoment, groupDigits, roundDecimal, showDecimal } from '@/shared/format/format';
import { useDebounced } from '@/shared/hooks/useDebounced';

type Rule = Schemas['IdeaExitRuleResponse'];
type Bucket = Schemas['BucketStatisticsResponse'];
type Window = Schemas['ExitRuleWindowResponse'];
type OutOfSample = Schemas['ExitRuleOutOfSampleResponse'];

const intervals = [
  { value: '5', label: '5 мин' },
  { value: '15', label: '15 мин' },
  { value: '60', label: '1 ч' },
  { value: '240', label: '4 ч' },
] as const;

const targets = ['1', '1.5', '2', '2.5', '3', '3.5', '4', '4.5', '5'];

const cell = 'px-3 py-2';
const number = `${cell} text-right font-mono whitespace-nowrap`;
const switchButton =
  'h-8 px-3 text-sm text-muted first:rounded-l-lg last:rounded-r-lg hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text';
const control = 'h-8 rounded-lg border border-line bg-panel px-2 text-sm';
const button =
  'h-9 rounded-lg px-4 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-40';

const percent = (share: string) => `${roundDecimal(shiftDecimal(share, 2), 0)} %`;
const inR = (value: string) => `${showDecimal(value, 2, true)} R`;

const candidateOf = (rule: Rule): ExitRuleCandidate => ({
  type: rule.type,
  interval: rule.interval,
  life: Number(rule.life),
  target: rule.target,
  window: rule.window === null ? null : Number(rule.window),
});

/** The rule over a column: «24 · 2R · 4». */
function ruleShort(rule: ExitRuleCandidate): string {
  return [
    String(rule.life),
    ...(rule.target === null ? [] : [`${rule.target}R`]),
    ...(rule.window === null ? [] : [String(rule.window)]),
  ].join(' · ');
}

/** The rule as one line: «срок 24 свечи · цель 2R · окно входа 4 свечи». */
function ruleText(rule: ExitRuleCandidate): string {
  return [
    `срок ${candlesText(rule.life)}`,
    ...(rule.target === null ? [] : [`цель ${rule.target}R`]),
    ...(rule.window === null ? [] : [`окно входа ${candlesText(rule.window)}`]),
  ].join(' · ');
}

/**
 * «Идеи / Срок жизни и цель»: the exit rule of a type × interval, a candidate moved by fields and sliders with its preview against the
 * rule now (asked 300 ms after the last change), and «Применить», which applies it to the ideas made from then on.
 */
export function IdeasExitRulesPage() {
  const [type, setType] = useState('pullback');
  const [interval, setInterval] = useState('15');
  const rules = useQuery(exitRulesQuery());
  const rule = rules.data?.rules.find((item) => item.type === type && item.interval === interval);
  const grid = (rules.data?.grids.find((item) => item.interval === interval)?.candles ?? []).map(
    Number,
  );

  return (
    <section className="space-y-5">
      <nav aria-label="Путь" className="text-sm text-muted">
        <Link to="/ideas" className="hover:text-text">
          Идеи
        </Link>{' '}
        / <span className="text-text">Срок жизни и цель</span>
      </nav>
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Срок жизни и цель</h1>
        <p className="text-sm text-muted">
          Правило выхода типа идеи на ТФ: сколько свечей идея живёт после входа, где её цель и
          сколько ждёт входа «Откат» и «Стоп-вход». Расчёт идёт по сигналам прогона всех сетапов,
          пар и категорий, без нового прогона.
        </p>
      </header>
      <div className="flex flex-wrap items-center gap-3">
        <div role="group" aria-label="Тип" className="flex flex-wrap rounded-lg border border-line">
          {ideaTypes.map((choice) => (
            <button
              key={choice.value}
              type="button"
              aria-pressed={type === choice.value}
              onClick={() => setType(choice.value)}
              className={switchButton}
            >
              {choice.label}
            </button>
          ))}
        </div>
        <div role="group" aria-label="ТФ" className="flex rounded-lg border border-line">
          {intervals.map((choice) => (
            <button
              key={choice.value}
              type="button"
              aria-pressed={interval === choice.value}
              onClick={() => setInterval(choice.value)}
              className={switchButton}
            >
              {choice.label}
            </button>
          ))}
        </div>
      </div>
      {rules.isError && <p role="alert">Не удалось загрузить правила выхода.</p>}
      {rules.isPending && <p className="text-sm text-muted">Загрузка правил…</p>}
      {rule && grid.length > 0 && (
        <RuleEditor key={`${type} ${interval} ${rule.appliedAt ?? ''}`} rule={rule} grid={grid} />
      )}
    </section>
  );
}

function RuleEditor({ rule, grid }: { rule: Rule; grid: readonly number[] }) {
  const current = candidateOf(rule);
  const [candidate, setCandidate] = useState(current);
  const asked = useDebounced(candidate, 300);
  const preview = useQuery(exitRulePreviewQuery(asked));
  const check = useQuery(exitRuleOutOfSampleQuery(asked));
  const apply = useApplyExitRule();
  const same = sameRule(candidate, current);
  const label = `«${typeLabel(rule.type)}» ${intervals.find((item) => item.value === rule.interval)?.label ?? rule.interval}`;

  return (
    <>
      <p className="text-sm">
        <span className="text-muted">Сейчас: </span>
        {ruleText(current)}
        <span className="text-faint">
          {' '}
          ·{' '}
          {rule.appliedAt === null
            ? 'по умолчанию'
            : `применено ${formatMoment(rule.appliedAt)} UTC`}
        </span>
      </p>
      <div className="grid gap-4 rounded-xl border border-line bg-panel px-5 py-4 md:grid-cols-3">
        <CandlesField
          label="Срок жизни"
          interval={rule.interval}
          grid={grid}
          value={candidate.life}
          onChange={(life) => setCandidate({ ...candidate, life })}
        />
        {candidate.target !== null && (
          <label className="space-y-2 text-sm">
            <span className="block text-[11px] tracking-wide text-faint uppercase">Цель</span>
            <input
              type="range"
              aria-label="Цель, ползунок"
              min={0}
              max={targets.length - 1}
              value={targets.findIndex((target) => Number(target) === Number(candidate.target))}
              onChange={(event) =>
                setCandidate({ ...candidate, target: targets[Number(event.target.value)]! })
              }
              className="w-full"
            />
            <select
              aria-label="Цель, R"
              value={
                targets.find((target) => Number(target) === Number(candidate.target)) ??
                candidate.target
              }
              onChange={(event) => setCandidate({ ...candidate, target: event.target.value })}
              className={control}
            >
              {targets.map((target) => (
                <option key={target} value={target}>
                  {target}R
                </option>
              ))}
            </select>
          </label>
        )}
        {candidate.window !== null && (
          <CandlesField
            label="Окно входа"
            interval={rule.interval}
            grid={grid}
            value={candidate.window}
            onChange={(window) => setCandidate({ ...candidate, window })}
          />
        )}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          disabled={same || apply.isPending}
          onClick={() => apply.mutate(candidate)}
          className={`${button} bg-accent text-on-accent`}
        >
          Применить
        </button>
        <button
          type="button"
          disabled={same}
          onClick={() => setCandidate(current)}
          className={`${button} border border-line`}
        >
          Сбросить
        </button>
        <p className="text-sm text-muted">
          Подействует на новые идеи {label}; открытые доживают по своему правилу. Корзины
          пересчитаются в течение 2 минут.
        </p>
      </div>
      {apply.isError && <p role="alert">Не удалось применить правило.</p>}
      {preview.isError && <p role="alert">Не удалось посчитать кандидата.</p>}
      {preview.data && (
        <>
          <header className="flex flex-wrap items-baseline gap-x-3">
            <h2 className="font-semibold">Было → станет</h2>
            <p className="text-xs text-faint">
              по прошлым сигналам {label} всех сетапов и пар · после комиссий
            </p>
          </header>
          <div className="grid gap-4 lg:grid-cols-2">
            <WindowTable
              label="Все 90 дней"
              window={preview.data.last90}
              now={ruleShort(current)}
              then={ruleShort(asked)}
            />
            <div className="space-y-2">
              <WindowTable
                label="Последние 30 дней"
                window={preview.data.last30}
                now={ruleShort(current)}
                then={ruleShort(asked)}
              />
              {overfitWarning(preview.data.last90, preview.data.last30) && (
                <p role="status" className="text-sm text-warn">
                  {overfitWarning(preview.data.last90, preview.data.last30)}
                </p>
              )}
            </div>
          </div>
          {check.isError ? (
            <p role="alert">Не удалось проверить кандидата вне выборки.</p>
          ) : check.data ? (
            <OutOfSampleBlock check={check.data} candidate={asked} />
          ) : (
            <p className="text-sm text-muted">Проверка считается…</p>
          )}
          <div className="grid gap-4 lg:grid-cols-2">
            <section className="space-y-2 rounded-xl border border-line bg-panel px-5 py-4">
              <h2 className="text-sm font-semibold">Ожидание от срока жизни</h2>
              <p className="text-xs text-faint">
                {[
                  ...(asked.target === null ? [] : [`при цели ${asked.target}R`]),
                  ...(asked.window === null ? [] : [`окне входа ${candlesText(asked.window)}`]),
                ].join(' и ')}
                {asked.target === null && asked.window === null ? '' : ' · '}все 90 дней
              </p>
              <LifeChart
                lives={preview.data.lives}
                interval={rule.interval}
                current={current.life}
                chosen={candidate.life}
              />
              <p className="flex flex-wrap gap-x-4 text-xs text-muted">
                <span className="text-series-1">— на вход</span>
                <span className="text-series-2">— на сигнал (неисполненные лимитки = 0 R)</span>
                <span className="text-faint">
                  свечей {intervals.find((item) => item.value === rule.interval)?.label}, шкала
                  логарифмическая
                </span>
              </p>
            </section>
            <section className="space-y-2 rounded-xl border border-line bg-panel px-5 py-4">
              <h2 className="text-sm font-semibold">
                Кривая MFE: как далеко цена уходит в нашу сторону
              </h2>
              <p className="text-xs text-faint">
                доля входов, дошедших до +x R раньше стопа за срок {candlesText(asked.life)} · все
                90 дней
              </p>
              <MfeChart
                points={preview.data.mfe}
                current={current.target}
                chosen={candidate.target}
              />
              <p className="flex flex-wrap gap-x-4 text-xs text-muted">
                <span className="text-series-1">— дошли до +x R</span>
                <span>- - нужно, чтобы цель x R окупилась после комиссий</span>
              </p>
            </section>
          </div>
        </>
      )}
    </>
  );
}

/** Candles of the grid as a slider and a list, and a time in hours rounded up to the grid with what the rounding did. */
function CandlesField({
  label,
  interval,
  grid,
  value,
  onChange,
}: {
  label: string;
  interval: string;
  grid: readonly number[];
  value: number;
  onChange: (candles: number) => void;
}) {
  const [hours, setHours] = useState('');
  const [note, setNote] = useState<string | null>(null);

  useEffect(() => {
    setHours('');
  }, [value]);

  return (
    <div className="space-y-2 text-sm">
      <span className="block text-[11px] tracking-wide text-faint uppercase">{label}</span>
      <input
        type="range"
        aria-label={`${label}, ползунок`}
        min={0}
        max={grid.length - 1}
        value={Math.max(0, grid.indexOf(value))}
        onChange={(event) => {
          setNote(null);
          onChange(grid[Number(event.target.value)]!);
        }}
        className="w-full"
      />
      <div className="flex items-center gap-2">
        <select
          aria-label={`${label}, свечей`}
          value={value}
          onChange={(event) => {
            setNote(null);
            onChange(Number(event.target.value));
          }}
          className={control}
        >
          {grid.map((candles) => (
            <option key={candles} value={candles}>
              {candles}
            </option>
          ))}
        </select>
        <span className="text-muted">⇄</span>
        <input
          aria-label={`${label}, часов`}
          inputMode="decimal"
          placeholder={durationText(value, interval)}
          value={hours}
          onChange={(event) => setHours(event.target.value)}
          onBlur={() => {
            const rounded = candlesOfHours(hours, interval, grid);

            if (rounded) {
              setNote(rounded.note);
              onChange(rounded.candles);
            }
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.currentTarget.blur();
            }
          }}
          className={`${control} w-24 text-right font-mono`}
        />
        <span className="text-muted">ч</span>
      </div>
      {note && <p className="text-xs text-faint">{note}</p>}
    </div>
  );
}

function shareInterval(low: string | null, high: string | null): string {
  return low === null || high === null
    ? ''
    : ` (${roundDecimal(shiftDecimal(low, 2), 0)}–${roundDecimal(shiftDecimal(high, 2), 0)} %)`;
}

const rows: readonly { label: string; value: (bucket: Bucket) => string }[] = [
  { label: 'Сигналов', value: (bucket) => groupDigits(String(bucket.signals ?? bucket.cases)) },
  {
    label: 'Вход исполнен',
    value: (bucket) => (bucket.entryShare === null ? '—' : percent(bucket.entryShare)),
  },
  {
    label: 'Из входов: цель · стоп · истекла',
    value: (bucket) =>
      [bucket.targets, bucket.stops, bucket.expired]
        .map((count) => `${Math.round((Number(count) * 100) / Number(bucket.cases))} %`)
        .join(' · '),
  },
  {
    label: 'Вероятность цели',
    value: (bucket) =>
      bucket.probability === null
        ? 'мало данных'
        : `${percent(bucket.probability)}${shareInterval(bucket.probabilityLow, bucket.probabilityHigh)}`,
  },
  {
    label: 'Ожидание на вход',
    value: (bucket) =>
      `${bucket.badge === null ? '' : `${badgeSymbol(bucket.badge)} `}${inR(bucket.expectancy)}`,
  },
  {
    label: 'Ожидание на сигнал',
    value: (bucket) => (bucket.signalExpectancy === null ? '—' : inR(bucket.signalExpectancy)),
  },
];

function WindowTable({
  label,
  window,
  now,
  then,
}: {
  label: string;
  window: Window;
  now: string;
  then: string;
}) {
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-semibold">{label}</h2>
      <table aria-label={label} className="w-full text-sm">
        <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th scope="col" className={cell} />
            <th scope="col" className={`${cell} text-right`}>
              было · {now}
            </th>
            <th scope="col" className={`${cell} text-right text-accent`}>
              станет · {then}
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {rows.map((row) => (
            <tr key={row.label}>
              <th scope="row" className={`${cell} text-left font-normal text-muted`}>
                {row.label}
              </th>
              <td className={number}>
                {window.current ? row.value(window.current) : 'нет входов'}
              </td>
              <td className={number}>
                {window.candidate ? row.value(window.candidate) : 'нет входов'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** The expectancy per entry of a part with its badge, its interval or «мало данных» under 30 entries, and the entries. */
function partText(bucket: Bucket | null): string {
  if (!bucket) {
    return 'нет входов';
  }

  return [
    `${bucket.badge === null ? '' : `${badgeSymbol(bucket.badge)} `}${inR(bucket.expectancy)}`,
    bucket.expectancyLow === null || bucket.expectancyHigh === null
      ? 'мало данных'
      : `от ${showDecimal(bucket.expectancyLow, 2, true)} до ${showDecimal(bucket.expectancyHigh, 2, true)}`,
    counted(Number(bucket.cases), 'вход', 'входа', 'входов'),
  ].join(' · ');
}

/**
 * «Проверка вне выборки»: the candidate and the best rule of the grid on the days 90 to 30 ago it was chosen on, both on the last 30 days, and
 * the probability of overfitting of the grid.
 */
function OutOfSampleBlock({
  check,
  candidate,
}: {
  check: OutOfSample;
  candidate: ExitRuleCandidate;
}) {
  const overfitting = overfittingText(check.overfitting);

  return (
    <section className="space-y-2">
      <header className="flex flex-wrap items-baseline gap-x-3">
        <h2 className="font-semibold">Проверка вне выборки</h2>
        <p className="text-xs text-faint">
          лучшее правило ищется только на подборе и проверяется на днях, которых при выборе не
          видели · сетка — {counted(Number(check.rules), 'правило', 'правила', 'правил')} (сроки ×
          цели при окне входа кандидата) · ожидание на вход с интервалом по часам срабатывания
        </p>
      </header>
      <table aria-label="Проверка вне выборки" className="w-full text-sm">
        <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th scope="col" className={cell} />
            <th scope="col" className={`${cell} text-right`}>
              подбор: с {formatMoment(check.selectionFrom)}
            </th>
            <th scope="col" className={`${cell} text-right`}>
              проверка: {formatMoment(check.checkFrom)} – {formatMoment(check.to)}
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          <tr>
            <th scope="row" className={`${cell} text-left font-normal text-muted`}>
              кандидат · {ruleShort(candidate)} · подбор до{' '}
              {formatMoment(check.candidate.selectionTo)}
            </th>
            <td className={number}>{partText(check.candidate.selection)}</td>
            <td className={number}>{partText(check.candidate.check)}</td>
          </tr>
          {check.best ? (
            <tr>
              <th scope="row" className={`${cell} text-left font-normal text-muted`}>
                лучшее на подборе ·{' '}
                {ruleShort({
                  ...candidate,
                  life: Number(check.best.life),
                  target: check.best.target,
                })}{' '}
                · подбор до {formatMoment(check.best.selectionTo)}
              </th>
              <td className={number}>{partText(check.best.selection)}</td>
              <td className={number}>{partText(check.best.check)}</td>
            </tr>
          ) : (
            <tr>
              <td colSpan={3} className={`${cell} text-muted`}>
                на подборе нет правила с 30 входами
              </td>
            </tr>
          )}
        </tbody>
      </table>
      <p className="text-sm">{overfitting.text}</p>
      <p className="text-xs text-faint">
        90 дней делятся на 6 блоков по 15 дней; в каждом из 20 разбиений пополам лучшее правило
        одной половины ищет своё место на другой. Окно входа не перебирается, так что это нижняя
        оценка.
      </p>
      {overfitting.warning && <p className="text-sm text-warn">{overfitting.warning}</p>}
    </section>
  );
}
