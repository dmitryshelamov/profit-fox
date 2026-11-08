import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { Fragment, useState } from 'react';

import { type ParameterDef, parameterMeta } from '@/modules/bots';
import { ApiError, type Schemas } from '@/shared/api/client';
import { isDecimalText } from '@/shared/api/jsonNumber';
import type { FieldErrors } from '@/shared/api/problems';
import { formatFullMoment, groupDigits } from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';

import { createBacktest } from '../api';
import { backtestFieldErrors } from '../model/backtestProblems';
import {
  combinationTotal,
  type ParameterInput,
  parametersBody,
  valueCountOf,
  valueInput,
} from '../model/parameterInputs';
import { periodEndingAt, type PeriodLength } from '../model/period';

const input =
  'block h-9 w-full rounded-md border bg-app px-3 font-mono text-sm text-text placeholder:text-faint';
const heading = 'text-[11px] font-semibold tracking-wide text-faint uppercase';

const quickPeriods: { title: string; length: PeriodLength }[] = [
  { title: '30 дней', length: 'month' },
  { title: '90 дней', length: 'quarter' },
  { title: '1 год', length: 'year' },
];

const simulationFields = [
  { key: 'balance', title: 'Баланс' },
  { key: 'limitFeePercent', title: 'Комиссия лимитки, %' },
  { key: 'marketFeePercent', title: 'Комиссия рынка, %' },
  { key: 'slippagePercent', title: 'Проскальзывание, %' },
] as const;

type SimulationKey = (typeof simulationFields)[number]['key'];

/** `2026-10-03 15:00`: a moment of the form, in the local zone of the browser. */
const momentText = (date: Date) => formatFullMoment(date.toISOString());

const localMoment = /^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$/;

/**
 * The text of a period field as the API takes it: a local moment turned into UTC. Text that is no moment goes as typed
 * and is rejected by the server on its field.
 */
function isoOf(text: string): string {
  const parts = localMoment.exec(text.trim());

  if (parts === null) {
    return text.trim();
  }

  const [year, month, day, hours, minutes] = parts.slice(1).map(Number) as [
    number,
    number,
    number,
    number,
    number,
  ];

  return new Date(year, month - 1, day, hours, minutes).toISOString();
}

interface Bot {
  readonly interval: string;
  readonly parameterValues: Schemas['JsonObject'];
}

/** The form of a new backtest of a bot: the period, a value or a range for each parameter, and the simulation. */
export function BacktestForm({
  botId,
  bot,
  declared,
  now,
}: {
  botId: string;
  bot: Bot;
  declared: readonly ParameterDef[];
  /** The moment the form was opened, in milliseconds. */
  now: number;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const initial = periodEndingAt(new Date(now), bot.interval, 'month');
  const [from, setFrom] = useState(momentText(initial.from));
  const [to, setTo] = useState(momentText(initial.to));
  const [inputs, setInputs] = useState<Record<string, ParameterInput>>({});
  const [simulation, setSimulation] = useState<Partial<Record<SimulationKey | 'mode', string>>>({});
  const [simulationOpen, setSimulationOpen] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({ fields: {}, form: [] });
  const [busy, setBusy] = useState(false);

  const inputOf = (name: string) => inputs[name] ?? valueInput;
  const change = (name: string, patch: Partial<ParameterInput>) =>
    setInputs({ ...inputs, [name]: { ...inputOf(name), ...patch } });
  const counts = declared.map(({ name }) => ({ name, count: valueCountOf(inputOf(name)) }));
  const total = combinationTotal(counts);
  const fieldError = (...keys: string[]) =>
    keys.flatMap((key) => errors.fields[key] ?? []).join(' ');
  const border = (...keys: string[]) => (fieldError(...keys) ? 'border-err' : 'border-line');
  const botValues = bot.parameterValues as Record<string, unknown>;

  function setPeriod(length: PeriodLength) {
    const period = periodEndingAt(new Date(now), bot.interval, length);

    setFrom(momentText(period.from));
    setTo(momentText(period.to));
  }

  async function start() {
    setErrors({ fields: {}, form: [] });
    const filled = simulationFields.filter(({ key }) => (simulation[key] ?? '').trim() !== '');
    const notNumbers = filled.filter(({ key }) => !isDecimalText(simulation[key]!.trim()));

    if (notNumbers.length > 0) {
      setErrors({
        fields: Object.fromEntries(
          notNumbers.map(({ key }) => [`simulation.${key}`, ['Введите число.']]),
        ),
        form: [],
      });
      setSimulationOpen(true);

      return;
    }

    setBusy(true);
    const mode = simulation.mode ?? '';
    const body: Schemas['CreateBacktestBody'] = {
      from: isoOf(from),
      to: isoOf(to),
      parameters: parametersBody(declared, inputs),
    };

    if (filled.length > 0 || mode !== '') {
      body.simulation = {
        ...Object.fromEntries(filled.map(({ key }) => [key, simulation[key]!.trim()])),
        ...(mode === '' ? {} : { limitFillMode: mode }),
      };
    }

    try {
      const id = await createBacktest(botId, body);

      // The bot page and the list of bots show cached lists for 30 seconds; the new backtest has to be in both.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['bots'], exact: true }),
        queryClient.invalidateQueries({ queryKey: ['bots', botId, 'backtests'] }),
      ]);
      await navigate({ to: '/backtests/$backtestId', params: { backtestId: id } });
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setErrors({ fields: {}, form: ['Не удалось связаться с сервером.'] });

        return;
      }

      const laidOut = backtestFieldErrors(error.errors);
      const known = Object.keys(laidOut.fields).length > 0 || laidOut.form.length > 0;

      setErrors(
        known
          ? laidOut
          : {
              fields: {},
              form: [
                error.status === 503
                  ? 'Биржа недоступна: правила инструмента получить не удалось. Бэктест не создан.'
                  : error.message,
              ],
            },
      );
      setSimulationOpen(Object.keys(laidOut.fields).some((key) => key.startsWith('simulation.')));
    } finally {
      setBusy(false);
    }
  }

  const problem = (...keys: string[]) =>
    fieldError(...keys) && <p className="mt-1 text-xs text-err">{fieldError(...keys)}</p>;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        void start();
      }}
    >
      <section className="space-y-3 rounded-xl border border-line bg-panel p-5">
        <div className="flex items-baseline gap-4">
          <h2 className={heading}>Период</h2>
          <span className="text-xs text-faint">
            По умолчанию — 30 дней до последней закрытой свечи
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <input
            aria-label="Начало периода"
            className={`${input} w-52! ${border('from', 'period')}`}
            value={from}
            onChange={(event) => setFrom(event.target.value)}
          />
          <span className="text-faint">—</span>
          <input
            aria-label="Конец периода"
            className={`${input} w-52! ${border('to', 'period')}`}
            value={to}
            onChange={(event) => setTo(event.target.value)}
          />
          {quickPeriods.map(({ title, length }) => (
            <Button key={title} onClick={() => setPeriod(length)}>
              {title}
            </Button>
          ))}
        </div>
        {problem('from', 'to', 'period')}
      </section>
      <section className="space-y-3 rounded-xl border border-line bg-panel p-5">
        <div className="flex items-baseline gap-4">
          <h2 className={heading}>Параметры</h2>
          <span className="text-xs text-faint">Не заданный параметр берёт значение бота</span>
        </div>
        <table className="w-full text-sm">
          <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
            <tr>
              <th className="py-2 font-semibold">Параметр</th>
              <th className="py-2 font-semibold">Задание</th>
              <th className="py-2 font-semibold">Min / значение</th>
              <th className="py-2 font-semibold">Max</th>
              <th className="py-2 font-semibold">Шаг</th>
              <th className="py-2 font-semibold">Значений</th>
            </tr>
          </thead>
          <tbody>
            {declared.map((parameter) => {
              const { name } = parameter;
              const current = inputOf(name);
              const count = counts.find((entry) => entry.name === name)!.count;
              const own = botValues[name];
              const hint =
                own !== undefined
                  ? `${String(own)} бот`
                  : `${String(parameter.default ?? '')} по умолчанию`;
              const key = `parameters.${name}`;

              return (
                <Fragment key={name}>
                  <tr className="align-top">
                    <th scope="row" className="py-2 pr-4 text-left font-normal">
                      <span className="block font-mono font-semibold">{name}</span>
                      <span className="text-xs text-faint">{parameterMeta(parameter)}</span>
                    </th>
                    <td className="py-2 pr-4">
                      <div className="inline-grid grid-cols-2 gap-1 rounded-md border border-line bg-app p-1">
                        {(['value', 'range'] as const).map((mode) => (
                          <button
                            key={mode}
                            type="button"
                            aria-pressed={current.mode === mode}
                            className={`h-7 rounded px-2 text-sm ${current.mode === mode ? 'bg-panel-2 font-bold' : 'text-muted'}`}
                            onClick={() => change(name, { mode })}
                          >
                            {mode === 'value' ? 'Значение' : 'Диапазон'}
                          </button>
                        ))}
                      </div>
                    </td>
                    {current.mode === 'value' ? (
                      <td colSpan={3} className="py-2 pr-4">
                        <input
                          aria-label={`${name} значение`}
                          className={`${input} ${border(key)}`}
                          placeholder={hint}
                          value={current.value}
                          onChange={(event) => change(name, { value: event.target.value })}
                        />
                        {problem(key)}
                      </td>
                    ) : (
                      <>
                        {(
                          [
                            ['min', 'min'],
                            ['max', 'max'],
                            ['step', 'шаг'],
                          ] as const
                        ).map(([field, title]) => (
                          <td key={field} className="py-2 pr-4">
                            <input
                              aria-label={`${name} ${title}`}
                              className={`${input} ${'problem' in count && count.problem ? 'border-err' : border(key)}`}
                              value={current[field]}
                              onChange={(event) => change(name, { [field]: event.target.value })}
                            />
                          </td>
                        ))}
                      </>
                    )}
                    <td className="py-4 font-mono">
                      <span aria-label={`${name} значений`}>
                        {'count' in count ? String(count.count) : '—'}
                      </span>
                    </td>
                  </tr>
                  {current.mode === 'range' && fieldError(key) && (
                    <tr>
                      <td colSpan={6}>{problem(key)}</td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
        {declared.map(({ name }) => {
          const count = counts.find((entry) => entry.name === name)!.count;

          return (
            'problem' in count &&
            count.problem && (
              <p key={name} className="text-xs text-err">
                {name}: {count.problem}
              </p>
            )
          );
        })}
      </section>
      <section className="rounded-xl border border-line bg-panel px-5 py-4 text-sm">
        <div className="flex items-center justify-between gap-4">
          <span className="text-muted">
            Режим поиска{' '}
            <span className="ml-2 rounded-full bg-[#2d323d] px-2 py-0.5 text-[11px] font-semibold text-text">
              Полный перебор
            </span>
          </span>
          <span className={`font-bold ${'total' in total ? '' : 'text-err'}`}>
            {'total' in total
              ? `Комбинаций: ${groupDigits(String(total.total))}`
              : `Комбинаций: ? — исправьте диапазон ${total.unresolved}`}
          </span>
          <span className="text-xs text-faint">лимит задаёт сервер</span>
        </div>
        {problem('parameters', 'searchMode')}
      </section>
      <details
        open={simulationOpen}
        onToggle={(event) => setSimulationOpen(event.currentTarget.open)}
        className="rounded-xl border border-line bg-panel px-5 py-4"
      >
        <summary className="cursor-pointer text-sm font-semibold">Настройки симуляции</summary>
        <p className="mt-2 text-xs text-faint">
          Пустое поле — значение по умолчанию сервиса, пустой баланс — баланс бота.
        </p>
        <div className="mt-3 grid grid-cols-5 gap-4">
          {simulationFields.map(({ key, title }) => (
            <div key={key}>
              <label htmlFor={`simulation-${key}`} className="text-[13px] text-muted">
                {title}
              </label>
              <input
                id={`simulation-${key}`}
                inputMode="decimal"
                className={`${input} ${border(`simulation.${key}`)}`}
                value={simulation[key] ?? ''}
                onChange={(event) => setSimulation({ ...simulation, [key]: event.target.value })}
              />
              {problem(`simulation.${key}`)}
            </div>
          ))}
          <div>
            <label htmlFor="simulation-mode" className="text-[13px] text-muted">
              Исполнение лимиток
            </label>
            <select
              id="simulation-mode"
              className={`${input} ${border('simulation.limitFillMode')}`}
              value={simulation.mode ?? ''}
              onChange={(event) => setSimulation({ ...simulation, mode: event.target.value })}
            >
              <option value="">по умолчанию</option>
              <option value="touch">касание</option>
              <option value="breakout">пробой</option>
            </select>
            {problem('simulation.limitFillMode')}
          </div>
        </div>
      </details>
      {errors.form.length > 0 && (
        <div
          role="alert"
          className="rounded-md border border-[#5a2a2e] bg-err-bg p-3 text-sm text-err"
        >
          {errors.form.map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      )}
      <div className="flex items-center justify-between gap-4">
        <p className="text-xs text-faint">
          Правила инструмента запрашиваются у биржи при запуске; если биржа не отвечает, бэктест не
          создаётся.
        </p>
        <div className="flex gap-2.5">
          <Link
            to="/bots/$botId"
            params={{ botId }}
            className="inline-flex h-8 items-center rounded-md border border-line px-3 text-sm font-semibold hover:bg-panel-2"
          >
            Отмена
          </Link>
          <Button look="primary" type="submit" disabled={busy}>
            Запустить
          </Button>
        </div>
      </div>
    </form>
  );
}
