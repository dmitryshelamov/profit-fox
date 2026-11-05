import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { accountsQuery } from '@/modules/exchange-accounts';
import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';

import { type SettingsInput, tradingSettingsQuery, useSaveSettings } from '../api';
import { formOfSettings, settingsFieldErrors } from '../model/settingsForm';

type Form = { [K in keyof SettingsInput]: string };

const control = 'h-9 w-full rounded-lg border border-line bg-panel px-2 text-sm';

function Field({
  label,
  value,
  errors,
  onChange,
}: {
  label: string;
  value: string;
  errors: readonly string[] | undefined;
  onChange: (value: string) => void;
}) {
  return (
    <label className="block space-y-1 text-sm">
      <span className="text-muted">{label}</span>
      <input
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
    </label>
  );
}

function SettingsForm({ initial }: { initial: Form }) {
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
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field
          label="Риск на сделку, USDT"
          value={form.risk}
          errors={errors.fields.risk}
          onChange={(risk) => set({ risk })}
        />
        <Field
          label="Не больше суммы на сделку, USDT"
          value={form.maxAmount}
          errors={errors.fields.maxAmount}
          onChange={(maxAmount) => set({ maxAmount })}
        />
        <Field
          label="Не больше открытых сделок"
          value={form.maxOpenTrades}
          errors={errors.fields.maxOpenTrades}
          onChange={(maxOpenTrades) => set({ maxOpenTrades })}
        />
        <Field
          label="Не больше общего риска, USDT"
          value={form.maxTotalRisk}
          errors={errors.fields.maxTotalRisk}
          onChange={(maxTotalRisk) => set({ maxTotalRisk })}
        />
        <Field
          label="Минимальный R:R после комиссий"
          value={form.minRiskReward}
          errors={errors.fields.minRiskReward}
          onChange={(minRiskReward) => set({ minRiskReward })}
        />
      </div>
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

/** The settings of trading by ideas, saved on the server: the account by default, the risk, the limits and the lowest R:R. */
export function SettingsPanel() {
  const settings = useQuery(tradingSettingsQuery());

  return (
    <section
      aria-label="Настройки"
      className="space-y-3 rounded-xl border border-line bg-panel px-4 py-4"
    >
      <h2 className="text-sm font-semibold">Настройки</h2>
      {settings.isError && <p role="alert">Не удалось загрузить настройки.</p>}
      {settings.data && <SettingsForm initial={formOfSettings(settings.data)} />}
    </section>
  );
}
