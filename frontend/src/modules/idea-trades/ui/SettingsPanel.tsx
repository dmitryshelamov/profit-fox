import { useQuery } from '@tanstack/react-query';
import { type ReactNode, useId, useState } from 'react';

import { accountsQuery } from '@/modules/exchange-accounts';
import { ApiError, type Schemas } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';

import { type SettingsInput, tradingSettingsQuery, useSaveSettings } from '../api';
import {
  autosBudgetStateText,
  dayShare,
  dayStateText,
  openStateText,
  resumeText,
  riskStateText,
  sidesStateText,
} from '../model/guards';
import { formOfSettings, settingsFieldErrors } from '../model/settingsForm';

type Form = { [K in keyof SettingsInput]: string };

const control = 'h-9 w-full rounded-lg border border-line bg-panel px-2 text-sm';

function Field({
  label,
  value,
  errors,
  onChange,
  children,
}: {
  label: string;
  value: string;
  errors: readonly string[] | undefined;
  onChange: (value: string) => void;
  /** What stands under the field: its state and its explanation. */
  children?: ReactNode;
}) {
  const id = useId();

  return (
    <div className="space-y-1 text-sm">
      <label htmlFor={id} className="block text-muted">
        {label}
      </label>
      <input
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={errors ? true : undefined}
        className={`${control} font-mono`}
      />
      {errors?.map((message) => (
        <span key={message} className="block text-xs text-err">
          {message}
        </span>
      ))}
      {children}
    </div>
  );
}

const hint = 'block text-xs text-faint';

/** The loss of the day against the limit as a bar; red once the stop has held. */
function DayBar({ guards }: { guards: Schemas['IdeaGuardsResponse'] }) {
  const share = dayShare(guards);

  return (
    <span className="block space-y-1">
      <span className="flex items-center gap-3">
        <span
          className={`font-mono text-xs ${guards.triggeredAt === null ? 'text-warn' : 'text-err'}`}
        >
          {dayStateText(guards)}
        </span>
        {share !== null && (
          <span aria-hidden className="h-1.5 flex-1 rounded-full bg-app">
            <span
              className={`block h-full rounded-full ${guards.triggeredAt === null ? 'bg-warn' : 'bg-err'}`}
              style={{ width: `${share * 100}%` }}
            />
          </span>
        )}
      </span>
      {resumeText(guards) && <span className="block text-xs text-err">{resumeText(guards)}</span>}
    </span>
  );
}

function SettingsForm({
  initial,
  guards,
}: {
  initial: Form;
  guards: Schemas['IdeaGuardsResponse'] | undefined;
}) {
  const [form, setForm] = useState(initial);
  const accounts = useQuery(accountsQuery);
  const save = useSaveSettings();
  const errors = settingsFieldErrors(
    save.error instanceof ApiError ? save.error.errors : undefined,
  );
  const set = (patch: Partial<Form>) => setForm((current) => ({ ...current, ...patch }));

  return (
    <form
      className="space-y-3"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate({ ...form, accountId: form.accountId === '' ? null : form.accountId });
      }}
    >
      <label className="block space-y-1 text-sm">
        <span className="text-muted">Аккаунт по умолчанию</span>
        <select
          value={form.accountId}
          onChange={(event) => set({ accountId: event.target.value })}
          aria-invalid={errors.fields.accountId ? true : undefined}
          className={control}
        >
          <option value="">не выбран</option>
          {accounts.data?.map((account) => (
            <option key={account.id} value={account.id}>
              {account.name}
            </option>
          ))}
        </select>
        {errors.fields.accountId?.map((message) => (
          <span key={message} className="block text-xs text-err">
            {message}
          </span>
        ))}
      </label>
      <span className={hint}>для ручных входов; у каждого автомата — свой</span>
      <h3 className="pt-2 text-[11px] tracking-wide text-faint uppercase">Сделка</h3>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field
          label="Сумма сделки по умолчанию, USDT"
          value={form.amount}
          errors={errors.fields.amount}
          onChange={(amount) => set({ amount })}
        >
          <span className={hint}>подставляется в окно входа и в новый автомат</span>
        </Field>
        <Field
          label="Максимум на одну сделку, USDT"
          value={form.maxAmount}
          errors={errors.fields.maxAmount}
          onChange={(maxAmount) => set({ maxAmount })}
        >
          <span className={hint}>
            больше вписать нельзя — защита от опечатки; и для ручных входов, и для автоматов
          </span>
        </Field>
        <Field
          label="Прибыль к убытку не меньше"
          value={form.minRiskReward}
          errors={errors.fields.minRiskReward}
          onChange={(minRiskReward) => set({ minRiskReward })}
        >
          <span className={hint}>
            1.5 — цель должна дать в полтора раза больше, чем заберёт стоп; для ручных входов
          </span>
        </Field>
        <Field
          label="Плечо на фьючерсах"
          value={form.leverage}
          errors={errors.fields.leverage}
          onChange={(leverage) => set({ leverage })}
        >
          <span className={hint}>
            ставится на символ перед входом; сумма сделки — это размер позиции, плечо уменьшает
            только залог; на споте не действует
          </span>
        </Field>
      </div>
      <h3 className="pt-2 text-[11px] tracking-wide text-faint uppercase">
        Защиты денег — все реальные сделки, ручные и автоматов
      </h3>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field
          label="Не больше открытых сделок"
          value={form.maxOpenTrades}
          errors={errors.fields.maxOpenTrades}
          onChange={(maxOpenTrades) => set({ maxOpenTrades })}
        >
          {guards && <span className={hint}>{openStateText(guards)}</span>}
        </Field>
        <Field
          label="Максимальный убыток, если сработают все стопы, USDT"
          value={form.maxTotalRisk}
          errors={errors.fields.maxTotalRisk}
          onChange={(maxTotalRisk) => set({ maxTotalRisk })}
        >
          {guards && <span className={hint}>{riskStateText(guards)}</span>}
        </Field>
      </div>
      <section aria-label="Защиты автоматов" className="space-y-3">
        <h3 className="flex items-center gap-2 pt-2 text-[11px] tracking-wide text-faint uppercase">
          Защиты автоматов — останавливают только входы автоматов с деньгами
          <span className="rounded bg-accent/20 px-1.5 py-px text-[10px] font-bold text-accent normal-case">
            новое
          </span>
        </h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <Field
            label="Дневной стоп по убытку, USDT"
            value={form.dailyLossLimit}
            errors={errors.fields.dailyLossLimit}
            onChange={(dailyLossLimit) => set({ dailyLossLimit })}
          >
            {guards && <DayBar guards={guards} />}
            <span className={hint}>
              С 00:00 UTC — закрытые сегодня реальные сделки, ручные и автоматов; при убытке выше
              предела автоматы не входят до следующих суток UTC. Ручные входы и открытые сделки он
              не трогает. Пустое поле — защита выключена.
            </span>
          </Field>
          <Field
            label="Не больше сделок в одну сторону"
            value={form.maxSameDirection}
            errors={errors.fields.maxSameDirection}
            onChange={(maxSameDirection) => set({ maxSameDirection })}
          >
            {guards && <span className={hint}>{sidesStateText(guards)}</span>}
            <span className={hint}>
              Ручные входы не останавливает. Пустое поле — защита выключена.
            </span>
          </Field>
          <Field
            label="Бюджет всех автоматов, USDT"
            value={form.autosBudget}
            errors={errors.fields.autosBudget}
            onChange={(autosBudget) => set({ autosBudget })}
          >
            {guards && <span className={hint}>{autosBudgetStateText(guards)}</span>}
            <span className={hint}>
              Сумма открытых реальных сделок всех автоматов. Ручные и учебные сделки не считаются и
              не останавливаются. Пустое поле — защита выключена.
            </span>
          </Field>
        </div>
      </section>
      {errors.form.map((message) => (
        <p key={message} role="alert" className="text-sm text-err">
          {message}
        </p>
      ))}
      {save.error && !(save.error instanceof ApiError && save.error.status === 400) && (
        <p role="alert" className="text-sm text-err">
          Не удалось сохранить настройки.
        </p>
      )}
      <div className="flex items-center gap-3">
        <Button type="submit" look="primary" disabled={save.isPending}>
          Сохранить
        </Button>
        {save.isSuccess && <span className="text-sm text-ok">Настройки сохранены</span>}
      </div>
    </form>
  );
}

/** The settings of trading by ideas, saved on the server: the account by default, the amount, the limits, the guards of the autos, the lowest R:R and the leverage on futures. The state of the guards, when it is given, stands under their fields. */
export function SettingsPanel({ guards }: { guards?: Schemas['IdeaGuardsResponse'] | undefined }) {
  const settings = useQuery(tradingSettingsQuery());

  return (
    <section
      aria-label="Настройки"
      className="space-y-3 rounded-xl border border-line bg-panel px-4 py-4"
    >
      <h2 className="text-sm font-semibold">Настройки</h2>
      {settings.isError && <p role="alert">Не удалось загрузить настройки.</p>}
      {settings.data && <SettingsForm initial={formOfSettings(settings.data)} guards={guards} />}
    </section>
  );
}
