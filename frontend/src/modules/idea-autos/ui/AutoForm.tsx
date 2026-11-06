import { useQuery } from '@tanstack/react-query';
import { type ReactNode, useId, useState } from 'react';

import { accountsQuery } from '@/modules/exchange-accounts';
import { ideaTypes } from '@/modules/ideas';
import { setupChoices } from '@/modules/market';
import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';

import { useSaveAuto } from '../api';
import {
  autoFieldErrors,
  type AutoFormValues,
  changedFields,
  changedText,
} from '../model/autoForm';
import { badgeChoices, badgeHint, categoryChoices, modes, sideChoices } from '../model/labels';

const control = 'h-9 w-full rounded-lg border border-line bg-app px-2 text-sm';
const chip = 'rounded-full border px-3 py-1.5 text-sm';

const intervalChoices = [
  { value: '5', label: '5 мин' },
  { value: '15', label: '15 мин' },
  { value: '60', label: '1 ч' },
  { value: '240', label: '4 ч' },
];

/** The type no auto takes: its entry waits for the price to leave a range. */
const neverTaken = 'stop_entry';

function Errors({ messages }: { messages: readonly string[] | undefined }) {
  return messages?.map((message) => (
    <span key={message} className="block text-xs text-err">
      {message}
    </span>
  ));
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-2 sm:grid-cols-[140px_1fr] sm:items-start">
      <span className="pt-1.5 text-sm text-muted">{label}</span>
      <div className="space-y-1">{children}</div>
    </div>
  );
}

/** A group of chips: any of the ticked ones is enough. */
function Chips({
  label,
  options,
  chosen,
  onChange,
  disabled,
  errors,
}: {
  label: string;
  options: readonly { value: string; label: string }[];
  chosen: readonly string[];
  onChange: (chosen: string[]) => void;
  /** A code the group shows but does not let be ticked, with its caption. */
  disabled?: { value: string; caption: string };
  errors: readonly string[] | undefined;
}) {
  return (
    <Row label={label}>
      <div role="group" aria-label={label} className="flex flex-wrap items-center gap-2">
        {options.map((option) =>
          option.value === disabled?.value ? (
            <span key={option.value} className="flex items-center gap-3">
              <button
                type="button"
                aria-pressed="false"
                aria-disabled="true"
                className={`${chip} cursor-not-allowed border-dashed border-line text-faint`}
              >
                {option.label}
              </button>
              <span className="text-xs text-faint">{disabled.caption}</span>
            </span>
          ) : (
            <button
              key={option.value}
              type="button"
              aria-pressed={chosen.includes(option.value)}
              onClick={() =>
                onChange(
                  chosen.includes(option.value)
                    ? chosen.filter((code) => code !== option.value)
                    : [...chosen, option.value],
                )
              }
              className={`${chip} ${chosen.includes(option.value) ? 'border-accent text-accent' : 'border-line text-text hover:bg-panel-2'}`}
            >
              {chosen.includes(option.value) ? '✓ ' : ''}
              {option.label}
            </button>
          ),
        )}
      </div>
      <Errors messages={errors} />
    </Row>
  );
}

/** A choice of one among a few, as a switch. */
function Switch({
  label,
  options,
  value,
  onChange,
  errors,
  hint,
}: {
  label: string;
  options: readonly { value: string; label: string }[];
  value: string;
  onChange: (value: string) => void;
  errors?: readonly string[] | undefined;
  hint?: string;
}) {
  return (
    <div className="space-y-1">
      <div
        role="radiogroup"
        aria-label={label}
        className="flex w-fit rounded-lg bg-app p-0.5 text-sm"
      >
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={value === option.value}
            onClick={() => onChange(option.value)}
            className={`h-8 rounded-md px-3 whitespace-nowrap ${
              value !== option.value
                ? 'text-muted hover:text-text'
                : option.value === 'live'
                  ? 'bg-accent font-bold text-on-accent'
                  : 'bg-panel-2 font-bold text-text'
            }`}
          >
            {option.label}
          </button>
        ))}
      </div>
      {hint && <p className="text-xs text-faint">{hint}</p>}
      <Errors messages={errors} />
    </div>
  );
}

function AccountField({
  value,
  onChange,
  errors,
}: {
  value: string;
  onChange: (value: string) => void;
  errors: readonly string[] | undefined;
}) {
  const id = useId();
  const accounts = useQuery(accountsQuery);

  return (
    <div className="space-y-1 text-sm">
      <label htmlFor={id} className="block text-muted">
        Аккаунт
      </label>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={errors ? true : undefined}
        className={control}
      >
        <option value="">не выбран</option>
        {accounts.data?.map((account) => (
          <option key={account.id} value={account.id}>
            {account.name}
          </option>
        ))}
      </select>
      <Errors messages={errors} />
    </div>
  );
}

/** A text field with its label tied by `htmlFor`, so the hint and the errors under it are not part of its name. */
function TextField({
  label,
  value,
  onChange,
  errors,
  hint,
  mono = false,
  narrow = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  errors: readonly string[] | undefined;
  hint?: string | undefined;
  mono?: boolean;
  narrow?: boolean;
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
        className={`${control} ${mono ? 'font-mono' : ''} ${narrow ? 'max-w-40' : ''}`}
      />
      {hint !== undefined && <span className="block text-xs text-faint">{hint}</span>}
      <Errors messages={errors} />
    </div>
  );
}

/**
 * The form of an auto, new or saved: its name, account, amount and mode, and the conditions it takes ideas by. «Сохранить» saves the
 * auto (and its mode, when it changed) and calls `onSaved`; «изменено: …» names what differs from the saved auto.
 */
export function AutoForm({
  id,
  initial,
  maxAmount,
  onSaved,
  onCancel,
}: {
  /** The saved auto; none for a new one. */
  id: string | null;
  /** The values the form opens with: those of the saved auto, or the defaults of a new one. */
  initial: AutoFormValues;
  /** «Не больше суммы на сделку» of the settings: the hint under the amount. */
  maxAmount: string | undefined;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const [form, setForm] = useState(initial);
  const save = useSaveAuto();
  const errors = autoFieldErrors(save.error instanceof ApiError ? save.error.errors : undefined);
  const set = (patch: Partial<AutoFormValues>) => setForm((current) => ({ ...current, ...patch }));
  const changed = changedText(changedFields(initial, form));
  const fieldErrors = errors.fields;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate({ id, form, savedMode: initial.mode }, { onSuccess: onSaved });
      }}
    >
      <section
        aria-label="Основное"
        className="space-y-3 rounded-xl border border-line bg-panel px-4 py-4"
      >
        <h2 className="font-semibold">Основное</h2>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <TextField
            label="Название"
            value={form.name}
            onChange={(name) => set({ name })}
            errors={fieldErrors.name}
          />
          <AccountField
            value={form.accountId}
            onChange={(accountId) => set({ accountId })}
            errors={fieldErrors.accountId}
          />
          <TextField
            label="Сумма на сделку, USDT"
            value={form.amount}
            onChange={(amount) => set({ amount })}
            errors={fieldErrors.amount}
            hint={maxAmount === undefined ? undefined : `не больше ${maxAmount} USDT`}
            mono
          />
          <div className="space-y-1 text-sm">
            <span className="text-muted">Режим</span>
            <Switch
              label="Режим"
              options={modes}
              value={form.mode}
              onChange={(mode) => set({ mode })}
            />
          </div>
        </div>
        <p className="text-xs text-faint">
          С деньгами — реальные ордера на аккаунте · Без денег — учебные сделки в журнале. Режим
          применяется при сохранении.
        </p>
      </section>
      <section
        aria-label="Условия"
        className="space-y-4 rounded-xl border border-line bg-panel px-4 py-4"
      >
        <h2 className="font-semibold">
          Условия{' '}
          <span className="text-xs font-normal text-faint">
            идея берётся, когда подходит под всё сразу; внутри строки — любой из отмеченных
          </span>
        </h2>
        <Chips
          label="Сетапы"
          options={setupChoices}
          chosen={form.setups}
          onChange={(setups) => set({ setups })}
          errors={fieldErrors.setups}
        />
        <Chips
          label="Типы идей"
          options={ideaTypes}
          chosen={form.types}
          onChange={(types) => set({ types })}
          disabled={{ value: neverTaken, caption: 'Стоп-вход автомат не берёт никогда' }}
          errors={fieldErrors.types}
        />
        <Chips
          label="ТФ"
          options={intervalChoices}
          chosen={form.intervals}
          onChange={(intervals) => set({ intervals })}
          errors={fieldErrors.intervals}
        />
        <Row label="Сторона">
          <Switch
            label="Сторона"
            options={sideChoices}
            value={form.side}
            onChange={(side) => set({ side })}
            errors={fieldErrors.side}
          />
        </Row>
        <Row label="Рынок">
          <Switch
            label="Рынок"
            options={categoryChoices}
            value={form.category}
            onChange={(category) => set({ category })}
            errors={fieldErrors.category}
          />
        </Row>
        <Row label="Значок">
          <Switch
            label="Значок"
            options={badgeChoices}
            value={form.badge}
            onChange={(badge) => set({ badge })}
            hint={badgeHint(form.badge === '' ? null : form.badge)}
            errors={fieldErrors.badge}
          />
        </Row>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Минимальный R:R"
            value={form.minRiskReward}
            onChange={(minRiskReward) => set({ minRiskReward })}
            errors={fieldErrors.minRiskReward}
            hint="после комиссий, у идеи на момент её рождения"
            mono
            narrow
          />
          <TextField
            label="Минимум кейсов"
            value={form.minCases}
            onChange={(minCases) => set({ minCases })}
            errors={fieldErrors.minCases}
            hint="N в корзине калибровки идеи"
            mono
            narrow
          />
        </div>
      </section>
      <p className="rounded-lg bg-run/10 px-4 py-3 text-xs text-run">
        Автомат берёт только идеи, родившиеся, пока он включён. Одна сделка на пару у автомата; с
        деньгами — одна на аккаунт и пару для всех автоматов: берёт тот, кто выше в списке. Если
        идея не прошла условия или защиту, в журнал «Отказы автоматов» пишется причина.
      </p>
      {errors.form.map((message) => (
        <p key={message} role="alert" className="text-sm text-err">
          {message}
        </p>
      ))}
      {save.error && !(save.error instanceof ApiError && save.error.status === 400) && (
        <p role="alert" className="text-sm text-err">
          Не удалось сохранить автомат.
        </p>
      )}
      <div className="flex flex-wrap items-center justify-end gap-3">
        {changed !== '' && <span className="text-sm text-accent">{changed}</span>}
        <Button look="ghost" onClick={onCancel}>
          Отмена
        </Button>
        <Button type="submit" look="primary" disabled={save.isPending}>
          Сохранить
        </Button>
      </div>
    </form>
  );
}
