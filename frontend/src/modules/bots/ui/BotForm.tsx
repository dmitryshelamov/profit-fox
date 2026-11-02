import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { instrumentsQuery, intervals } from '@/modules/market-data';
import { strategiesQuery, strategyQuery } from '@/modules/strategies';
import { ApiError, type Schemas } from '@/shared/api/client';
import type { FieldErrors } from '@/shared/api/problems';
import { Button } from '@/shared/ui/Button';

import { createBot, updateBot } from '../api';
import { botFieldErrors } from '../model/botProblems';
import { type ParameterDef, parameterMeta } from '../model/parameterMeta';
import { parameterValuesOf } from '../model/parameterValues';

const categories = ['spot', 'linear'] as const;
const input =
  'block h-9 w-full rounded-md border bg-app px-3 text-sm text-text placeholder:text-faint';
const labelText = 'text-[13px] text-muted';

function useDebounced<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);

    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}

function textsOf(values: unknown): Record<string, string> {
  return Object.fromEntries(
    Object.entries((values ?? {}) as Record<string, unknown>).map(([name, value]) => [
      name,
      String(value),
    ]),
  );
}

/** The form of a new bot, or of an existing one: its strategy is shown and cannot change. */
export function BotForm({
  bot,
  strategyId: initialStrategyId,
}: {
  bot?: Schemas['BotResponse'];
  strategyId?: string;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const strategies = useQuery(strategiesQuery);
  const [name, setName] = useState(bot?.name ?? '');
  const [strategyId, setStrategyId] = useState(bot?.strategyId ?? initialStrategyId ?? '');
  const [category, setCategory] = useState(bot?.category ?? 'spot');
  const [symbol, setSymbol] = useState(bot?.symbol ?? '');
  const [interval, setInterval] = useState(bot?.interval ?? '60');
  const [balance, setBalance] = useState(bot?.balance ?? '');
  const [texts, setTexts] = useState(() => textsOf(bot?.parameterValues));
  const [errors, setErrors] = useState<FieldErrors>({ fields: {}, form: [] });

  const strategy = useQuery({ ...strategyQuery(strategyId), enabled: strategyId !== '' });
  const declared = ((strategy.data?.definition as { parameters?: ParameterDef[] } | undefined)
    ?.parameters ?? []) as ParameterDef[];
  const typed = useDebounced(symbol, 200);
  const instruments = useQuery({
    ...instrumentsQuery(category, typed.trim()),
    enabled: typed.trim() !== '',
  });
  const quoteCoin = instruments.data?.find((instrument) => instrument.symbol === typed)?.quoteCoin;
  const strategyName = strategies.data?.find((item) => item.id === strategyId)?.name;
  const fieldError = (key: string) => errors.fields[key]?.join(' ');
  const fieldCount = Object.keys(errors.fields).length;

  function changeStrategy(id: string) {
    setStrategyId(id);
    setTexts({});
  }

  async function save() {
    setErrors({ fields: {}, form: [] });
    const values = {
      name,
      parameterValues: parameterValuesOf(declared, texts),
      category,
      symbol,
      interval,
      balance,
    };

    try {
      const id = bot
        ? (await updateBot(bot.id, values), bot.id)
        : await createBot({ ...values, strategyId });

      await queryClient.invalidateQueries({ queryKey: ['bots'] });
      await navigate({ to: '/bots/$botId', params: { botId: id } });
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setErrors({ fields: {}, form: ['Не удалось связаться с сервером.'] });

        return;
      }

      const laidOut = botFieldErrors(error.errors);

      setErrors(
        Object.keys(laidOut.fields).length > 0 || laidOut.form.length > 0
          ? laidOut
          : { fields: {}, form: [error.message] },
      );
    }
  }

  const border = (key: string) => (fieldError(key) ? 'border-err' : 'border-line');
  const problem = (key: string) =>
    fieldError(key) && <p className="mt-1 text-xs text-err">{fieldError(key)}</p>;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        void save();
      }}
    >
      <div className="grid grid-cols-2 gap-5">
        <section className="space-y-4 rounded-xl border border-line bg-panel p-5">
          <h2 className="font-semibold">Бот</h2>
          <div>
            <label htmlFor="bot-name" className={labelText}>
              Название
            </label>
            <input
              id="bot-name"
              className={`${input} ${border('name')}`}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            {problem('name')}
          </div>
          <div>
            <label htmlFor="bot-strategy" className={labelText}>
              Стратегия
            </label>
            {bot ? (
              <p id="bot-strategy" className="py-2 text-sm">
                {strategyName ?? '…'}
              </p>
            ) : (
              <select
                id="bot-strategy"
                className={`${input} ${border('strategyId')}`}
                value={strategyId}
                onChange={(event) => changeStrategy(event.target.value)}
              >
                <option value="" disabled>
                  Выберите стратегию
                </option>
                {strategies.data?.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            )}
            {problem('strategyId')}
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <span className={labelText}>Категория рынка</span>
              <div className="mt-1 grid grid-cols-2 gap-1 rounded-md border border-line bg-app p-1">
                {categories.map((item) => (
                  <button
                    key={item}
                    type="button"
                    aria-pressed={category === item}
                    className={`h-7 rounded font-mono text-sm ${category === item ? 'bg-panel-2 font-bold' : 'text-muted'}`}
                    onClick={() => setCategory(item)}
                  >
                    {item}
                  </button>
                ))}
              </div>
              {problem('category')}
            </div>
            <div>
              <label htmlFor="bot-interval" className={labelText}>
                Интервал
              </label>
              <select
                id="bot-interval"
                className={`${input} ${border('interval')}`}
                value={interval}
                onChange={(event) => setInterval(event.target.value)}
              >
                {intervals.map((item) => (
                  <option key={item.value} value={item.value}>
                    {item.label}
                  </option>
                ))}
              </select>
              {problem('interval')}
            </div>
            <div>
              <label htmlFor="bot-symbol" className={labelText}>
                Символ
              </label>
              <input
                id="bot-symbol"
                list="bot-symbols"
                autoComplete="off"
                className={`${input} font-mono ${border('symbol')}`}
                value={symbol}
                onChange={(event) => setSymbol(event.target.value)}
              />
              <datalist id="bot-symbols">
                {instruments.data?.map((instrument) => (
                  <option key={instrument.symbol} value={instrument.symbol}>
                    {instrument.baseCoin} / {instrument.quoteCoin}
                  </option>
                ))}
              </datalist>
              {problem('symbol')}
            </div>
            <div>
              <label htmlFor="bot-balance" className={labelText}>
                Баланс бота
              </label>
              <div className="relative">
                <input
                  id="bot-balance"
                  inputMode="decimal"
                  className={`${input} pr-16 font-mono ${border('balance')}`}
                  value={balance}
                  onChange={(event) => setBalance(event.target.value)}
                />
                {quoteCoin && (
                  <span className="absolute top-2 right-3 text-sm text-muted">{quoteCoin}</span>
                )}
              </div>
              {problem('balance')}
            </div>
          </div>
        </section>
        <section className="space-y-4 rounded-xl border border-line bg-panel p-5">
          <div>
            <h2 className="font-semibold">
              Параметры стратегии{strategyName ? ` ${strategyName}` : ''}
            </h2>
            <p className="text-xs text-faint">
              Пустое поле — значение по умолчанию последней версии стратегии.
            </p>
          </div>
          {declared.map((parameter) => (
            <div key={parameter.name}>
              <div className="flex items-baseline gap-3">
                <label htmlFor={`parameter-${parameter.name}`} className="font-mono font-semibold">
                  {parameter.name}
                </label>
                <span className="text-xs text-faint">{parameterMeta(parameter)}</span>
              </div>
              <input
                id={`parameter-${parameter.name}`}
                className={`${input} mt-1 font-mono ${border(`parameterValues.${parameter.name}`)}`}
                placeholder={parameter.default === undefined ? '' : String(parameter.default)}
                value={texts[parameter.name] ?? ''}
                onChange={(event) => setTexts({ ...texts, [parameter.name]: event.target.value })}
              />
              {problem(`parameterValues.${parameter.name}`)}
            </div>
          ))}
        </section>
      </div>
      {(fieldCount > 0 || errors.form.length > 0) && (
        <div
          role="alert"
          className="rounded-md border border-[#5a2a2e] bg-err-bg p-3 text-sm text-err"
        >
          {fieldCount > 0 && (
            <p>Бот не сохранён: ошибок проверки {fieldCount}, они отмечены на полях.</p>
          )}
          {errors.form.map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      )}
      <div className="flex justify-end gap-2.5">
        <Link
          to={bot ? '/bots/$botId' : '/bots'}
          params={bot ? { botId: bot.id } : undefined}
          className="inline-flex h-8 items-center rounded-md border border-line px-3 text-sm font-semibold hover:bg-panel-2"
        >
          Отмена
        </Link>
        <Button look="primary" type="submit">
          {bot ? 'Сохранить' : 'Создать бота'}
        </Button>
      </div>
    </form>
  );
}
