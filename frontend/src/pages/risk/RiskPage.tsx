import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';

import { accountsKey, listAccounts } from '@/api/accounts';
import type { RequestError } from '@/api/client';
import {
  getRiskSettings,
  halfWayText,
  liftHalt,
  type RiskSettings,
  riskSettingsKey,
  type RiskSettingsRequest,
  riskStateKey,
  saveRiskSettings,
} from '@/api/risk';
import { momentText } from '@/pages/market/format';
import { plural } from '@/pages/trades/format';
import { Button } from '@/shared/ui/Button';
import { Page, PageTitle } from '@/shared/ui/Page';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { HaltBanner, RiskStrip } from './RiskStrip';
import { useRiskState } from './useRiskState';

/** The parameters of the form, by the names the API gives them. */
type Field = keyof RiskSettingsRequest;
type Draft = Readonly<Record<Field, string>>;

const textOf = (value: number | string | null | undefined) =>
  value === null || value === undefined ? '' : String(value);

function draftOf(settings: RiskSettings): Draft {
  return {
    defaultAccountId: textOf(settings.defaultAccountId),
    defaultAmount: textOf(settings.defaultAmount),
    leverage: textOf(settings.leverage),
    maxAmount: textOf(settings.maxAmount),
    maxOpenTrades: textOf(settings.maxOpenTrades),
    maxTotalAmount: textOf(settings.maxTotalAmount),
    maxLossPercent: textOf(settings.maxLossPercent),
    maxDayLoss: textOf(settings.maxDayLoss),
    expiryMarginMinutes: textOf(settings.expiryMarginMinutes),
    entryWaitSeconds: textOf(settings.entryWaitSeconds),
    stopAttempts: textOf(settings.stopAttempts),
  };
}

const numericFields: readonly Field[] = [
  'defaultAmount',
  'leverage',
  'maxAmount',
  'maxOpenTrades',
  'maxTotalAmount',
  'maxLossPercent',
  'maxDayLoss',
  'expiryMarginMinutes',
  'entryWaitSeconds',
  'stopAttempts',
];

/** The fields of whole numbers: the server takes no fraction there. */
const wholeFields: readonly Field[] = [
  'leverage',
  'maxOpenTrades',
  'expiryMarginMinutes',
  'entryWaitSeconds',
  'stopAttempts',
];

/** The largest whole number the server takes. */
const maxWhole = 2_147_483_647;

/**
 * The request of a draft: an empty field is none; a text that is not a finite number, or a fraction or too large a number
 * in a field of whole numbers, is told at its field before anything is sent (review R2).
 */
function requestOf(draft: Draft): { request: RiskSettingsRequest; local: RequestError[] } {
  const local: RequestError[] = [];
  const values: Record<string, number | string | null> = {
    defaultAccountId: draft.defaultAccountId === '' ? null : draft.defaultAccountId,
  };
  for (const field of numericFields) {
    const text = draft[field].trim().replace(',', '.');
    const number = Number(text);
    if (text === '') {
      values[field] = null;
    } else if (!Number.isFinite(number)) {
      values[field] = null;
      local.push({ code: 'NotANumber', field, reason: 'Введите число' });
    } else if (
      wholeFields.includes(field) &&
      (!Number.isInteger(number) || Math.abs(number) > maxWhole)
    ) {
      values[field] = null;
      local.push({ code: 'NotWhole', field, reason: 'Введите целое число' });
    } else {
      values[field] = number;
    }
  }

  return { request: values as unknown as RiskSettingsRequest, local };
}

/**
 * «Риск» (change take-idea-trades, spec trade-risk «Раздел „Риск“», «Стоп-кран на экране»): the state of the module, the
 * halt with «Снять запрет», and all parameters in five groups saved by one button.
 */
export function RiskPage() {
  const state = useRiskState();
  const settings = useQuery({ queryKey: riskSettingsKey, queryFn: getRiskSettings });

  return (
    <Page>
      <PageTitle
        title="Риск"
        hint="Пределы и защита сделок по идеям. Параметры действуют на следующие входы; открытые сделки они не меняют."
      >
        {settings.data?.savedAt != null && (
          <span className="text-xs text-muted">Сохранено {momentText(settings.data.savedAt)}</span>
        )}
      </PageTitle>
      {state.isError && (
        <p role="alert" className="text-err">
          {state.error.message}
        </p>
      )}
      {state.data?.halt.haltedAt != null && <HaltWithLift halt={state.data.halt} />}
      {state.data &&
        (state.data.limitsSet ? (
          <RiskStrip state={state.data} />
        ) : (
          <p
            role="status"
            className="rounded-[10px] border border-line bg-panel px-6 py-5 text-warn"
          >
            Пределы не заданы: вход в идеи невозможен. Заполните параметры ниже и сохраните.
          </p>
        ))}
      {settings.isError && (
        <p role="alert" className="text-err">
          {settings.error.message}
        </p>
      )}
      {settings.data && <RiskForm saved={settings.data} />}
    </Page>
  );
}

function HaltWithLift({
  halt,
}: {
  readonly halt: NonNullable<ReturnType<typeof useRiskState>['data']>['halt'];
}) {
  const client = useQueryClient();
  const [refusal, setRefusal] = useState<string | null>(null);
  const lift = useMutation({
    mutationFn: liftHalt,
    onSuccess: (result) => {
      if (result.ok) {
        setRefusal(null);
        void client.invalidateQueries({ queryKey: riskStateKey });
      } else {
        setRefusal(result.reason);
      }
    },
    onError: (failure) => setRefusal(failure.message),
  });

  return (
    <HaltBanner halt={halt}>
      <div className="flex shrink-0 flex-col items-end gap-2">
        <Button disabled={lift.isPending} onClick={() => lift.mutate()}>
          Снять запрет
        </Button>
        {refusal !== null && (
          <p role="alert" className="max-w-[260px] text-right text-xs text-err">
            {refusal}
          </p>
        )}
      </div>
    </HaltBanner>
  );
}

function Group({
  title,
  hint,
  children,
  className = '',
}: {
  readonly title: string;
  readonly hint: string;
  readonly children: ReactNode;
  readonly className?: string;
}) {
  return (
    <section
      aria-label={title}
      className={`flex flex-col gap-4 rounded-[10px] border border-line bg-panel p-6 ${className}`}
    >
      <div className="flex flex-col gap-1">
        <h2 className="text-base font-bold">{title}</h2>
        <p className="text-xs text-muted">{hint}</p>
      </div>
      {children}
    </section>
  );
}

function RiskForm({ saved }: { readonly saved: RiskSettings }) {
  const client = useQueryClient();
  const accounts = useQuery({ queryKey: accountsKey, queryFn: listAccounts });
  const base = draftOf(saved);
  const [edits, setEdits] = useState<Partial<Record<Field, string>>>({});
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const value = (field: Field) => edits[field] ?? base[field];
  const changed = (Object.keys(edits) as Field[]).filter((field) => edits[field] !== base[field]);
  const draft = Object.fromEntries(
    (Object.keys(base) as Field[]).map((field) => [field, value(field)]),
  ) as Draft;

  const save = useMutation({
    mutationFn: () => {
      const { request, local } = requestOf(draft);
      if (local.length > 0) {
        return Promise.resolve({ ok: false as const, errors: local });
      }

      return saveRiskSettings(request);
    },
    onSuccess: (result) => {
      if (result.ok) {
        client.setQueryData(riskSettingsKey, result.value);
        void client.invalidateQueries({ queryKey: riskStateKey });
        setEdits({});
        setErrors([]);
      } else {
        setErrors(result.errors);
      }
    },
    onError: (failure) => setErrors([{ code: 'Failed', reason: failure.message }]),
  });

  const edit = (field: Field, text: string) => {
    setEdits((old) => ({ ...old, [field]: text }));
    setErrors((old) => old.filter((error) => error.field !== field));
  };
  const errorsAt = (field: Field) =>
    errors.filter((error) => error.field === field).map((error) => error.reason);
  const general = errors.filter((error) => !error.field).map((error) => error.reason);

  const numberField = (field: Field, label: string, unit: string, className = 'w-[180px]') => {
    const reasons = errorsAt(field);
    const id = `risk-${field}`;
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={id} className="text-xs text-muted">
          {label}
        </label>
        <TextField
          id={id}
          mono
          inputMode="decimal"
          autoComplete="off"
          unit={unit}
          value={value(field)}
          aria-invalid={reasons.length > 0}
          aria-describedby={reasons.length > 0 ? `${id}-error` : undefined}
          className={`${className} ${reasons.length > 0 ? 'border-err' : ''}`}
          onChange={(event) => edit(field, event.target.value)}
        />
        {reasons.length > 0 && (
          <span id={`${id}-error`} role="alert" className="text-xs text-err">
            {reasons.join('; ')}
          </span>
        )}
      </div>
    );
  };

  const leverage = Number(value('leverage'));
  const leverageKnown =
    value('leverage').trim() !== '' && Number.isInteger(leverage) && leverage >= 1;
  const amount = Number(value('defaultAmount'));
  const accountReasons = errorsAt('defaultAccountId');

  return (
    <form
      aria-label="Параметры риска"
      className="flex flex-col gap-5"
      onSubmit={(event) => {
        event.preventDefault();
        if (changed.length > 0) {
          save.mutate();
        }
      }}
    >
      <div className="grid grid-cols-2 gap-5 max-[1000px]:grid-cols-1">
        <Group
          title="Размер сделки"
          hint="С чем открывается диалог входа. Аккаунт и сумму в диалоге можно сменить, плечо — только здесь."
        >
          <div className="flex flex-wrap gap-4">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="risk-defaultAccountId" className="text-xs text-muted">
                Аккаунт по умолчанию
              </label>
              <SelectField
                id="risk-defaultAccountId"
                className="w-[200px]"
                value={value('defaultAccountId')}
                aria-invalid={accountReasons.length > 0}
                onChange={(event) => edit('defaultAccountId', event.target.value)}
              >
                <option value="">Не выбран</option>
                {(accounts.data ?? []).map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.name}
                  </option>
                ))}
              </SelectField>
              {accountReasons.length > 0 && (
                <span role="alert" className="text-xs text-err">
                  {accountReasons.join('; ')}
                </span>
              )}
            </div>
            {numberField('defaultAmount', 'Сумма по умолчанию', 'USDT', 'w-[160px]')}
            {numberField('leverage', 'Плечо', '×', 'w-[120px]')}
          </div>
          <p className="text-xs text-faint">
            Сумма — стоимость позиции.
            {leverageKnown && amount > 0 && (
              <>
                {' '}
                Залог при {leverage}× — около {Math.round(amount / leverage)} USDT.
              </>
            )}{' '}
            Плечо — целое от 1 до 20.
          </p>
        </Group>
        <Group
          title="Пределы входа"
          hint="Вход сверх любого из трёх отклоняется. Считаются сделки всех аккаунтов."
        >
          <div className="flex flex-wrap gap-4">
            {numberField('maxAmount', 'Сумма на сделку', 'USDT', 'w-[160px]')}
            {numberField('maxOpenTrades', 'Открытых сделок', 'шт.', 'w-[140px]')}
            {numberField('maxTotalAmount', 'Общая сумма в сделках', 'USDT', 'w-[170px]')}
          </div>
        </Group>
        <Group
          title="Защита сделки"
          hint="У каждой сделки стоп на бирже. Он срабатывает, даже когда стенд выключен."
        >
          {numberField('maxLossPercent', 'Потеря на сделку не больше', '%')}
          {leverageKnown && (
            <p className="text-xs text-muted">
              меньше {halfWayText(leverage)} % при плече {leverage}× — половина пути до ликвидации
            </p>
          )}
          <p className="text-xs text-muted">
            Стоп идеи ближе предела — действует стоп идеи. Стопа нет или он дальше — стоп встаёт на
            заданный процент от цены в момент клика.
          </p>
        </Group>
        <Group
          title="Стоп-кран"
          hint="Закрывает все сделки по рынку и запрещает вход, пока вы не снимете запрет."
        >
          {numberField('maxDayLoss', 'Убыток за день не больше', 'USDT')}
          <p className="text-xs text-muted">
            Считается: итоги сделок, закрытых за сутки UTC, плюс результат открытых сейчас по цене
            маркировки Bybit.
          </p>
          <p className="text-xs text-warn">
            Работает, только пока работает торговый Worker. Без него сделки защищены своими стопами
            на бирже.
          </p>
        </Group>
      </div>
      <Group
        title="Сроки и попытки"
        hint="Запас по времени и защита от сбоев. Пустыми не бывают: без правки действуют числа по умолчанию."
      >
        <div className="flex flex-wrap gap-8">
          <div className="flex flex-col gap-1.5">
            {numberField('expiryMarginMinutes', 'Не входить, если до срока идеи меньше', 'мин')}
            <span className="text-xs text-faint">от 1 до 60 · по умолчанию 1</span>
          </div>
          <div className="flex flex-col gap-1.5">
            {numberField('entryWaitSeconds', 'Ждать приёма входа', 'с')}
            <span className="text-xs text-faint">
              от 10 до 120 · по умолчанию 30. Bybit не принял заявку — отмена
            </span>
          </div>
          <div className="flex flex-col gap-1.5">
            {numberField('stopAttempts', 'Попыток вернуть стоп', 'шт.')}
            <span className="text-xs text-faint">
              от 1 до 10 · по умолчанию 3. Потом — закрытие по рынку
            </span>
          </div>
        </div>
      </Group>
      {general.length > 0 && (
        <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
          {general.join('; ')}
        </p>
      )}
      <div className="flex items-center justify-between gap-4 rounded-[10px] border border-line bg-panel px-6 py-4">
        {changed.length > 0 ? (
          <span role="status" className="text-warn">
            {changed.length}{' '}
            {plural(changed.length, [
              'несохранённая правка',
              'несохранённые правки',
              'несохранённых правок',
            ])}{' '}
            · вход считает по прежним
          </span>
        ) : (
          <span role="status" className="text-muted">
            Правок нет.
            {saved.savedAt != null && <> Параметры сохранены {momentText(saved.savedAt)}.</>}
          </span>
        )}
        <span className="flex gap-2">
          <Button
            disabled={changed.length === 0 || save.isPending}
            onClick={() => {
              setEdits({});
              setErrors([]);
            }}
          >
            Сбросить правки
          </Button>
          <Button type="submit" variant="primary" disabled={changed.length === 0 || save.isPending}>
            Сохранить
          </Button>
        </span>
      </div>
    </form>
  );
}
