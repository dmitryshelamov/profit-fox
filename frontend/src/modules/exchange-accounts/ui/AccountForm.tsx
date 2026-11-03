import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { ApiError } from '@/shared/api/client';
import type { FieldErrors } from '@/shared/api/problems';
import { Button } from '@/shared/ui/Button';

import { createAccount } from '../api';
import { accountFieldErrors, accountProblem } from '../model/accountProblems';

const input =
  'block h-9 w-full rounded-md border bg-app px-3 text-sm text-text placeholder:text-faint';
const labelText = 'text-[13px] text-muted';

/** The form of a new account; the secret is never kept after a failed save. */
export function AccountForm() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [apiSecret, setApiSecret] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({ fields: {}, form: [] });
  const [saving, setSaving] = useState(false);
  const fieldError = (key: string) => errors.fields[key]?.join(' ');

  async function save() {
    setErrors({ fields: {}, form: [] });
    setSaving(true);

    try {
      const id = await createAccount({ name, apiKey, apiSecret });

      await queryClient.invalidateQueries({ queryKey: ['exchange-accounts'] });
      await navigate({ to: '/accounts/$accountId', params: { accountId: id } });
    } catch (error) {
      setApiSecret('');
      const laidOut = error instanceof ApiError ? accountFieldErrors(error.errors) : undefined;

      setErrors(
        laidOut && (Object.keys(laidOut.fields).length > 0 || laidOut.form.length > 0)
          ? laidOut
          : { fields: {}, form: [accountProblem(error)] },
      );
    } finally {
      setSaving(false);
    }
  }

  const border = (key: string) => (fieldError(key) ? 'border-err' : 'border-line');
  const problem = (key: string) =>
    fieldError(key) && <p className="mt-1 text-xs text-err">{fieldError(key)}</p>;

  return (
    <form
      className="max-w-xl space-y-4 rounded-xl border border-line bg-panel p-5"
      onSubmit={(event) => {
        event.preventDefault();
        void save();
      }}
    >
      <div>
        <label htmlFor="account-name" className={labelText}>
          Название
        </label>
        <input
          id="account-name"
          className={`${input} ${border('name')}`}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        {problem('name')}
      </div>
      <div>
        <label htmlFor="account-api-key" className={labelText}>
          API key
        </label>
        <input
          id="account-api-key"
          autoComplete="off"
          spellCheck={false}
          className={`${input} font-mono ${border('apiKey')}`}
          value={apiKey}
          onChange={(event) => setApiKey(event.target.value)}
        />
        {problem('apiKey')}
      </div>
      <div>
        <label htmlFor="account-api-secret" className={labelText}>
          API secret
        </label>
        <input
          id="account-api-secret"
          type="password"
          autoComplete="off"
          className={`${input} font-mono ${border('apiSecret')}`}
          value={apiSecret}
          onChange={(event) => setApiSecret(event.target.value)}
        />
        {problem('apiSecret')}
      </div>
      <p className="text-xs text-faint">
        Bybit проверит ключ при сохранении: нужен ключ единого торгового аккаунта с правом на
        торговлю и без права на вывод средств. Secret хранится зашифрованным и больше не
        показывается.
      </p>
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
      <div className="flex justify-end gap-2.5">
        <Link
          to="/accounts"
          className="inline-flex h-8 items-center rounded-md border border-line px-3 text-sm font-semibold hover:bg-panel-2"
        >
          Отмена
        </Link>
        <Button look="primary" type="submit" disabled={saving}>
          {saving ? 'Проверяем ключ…' : 'Добавить аккаунт'}
        </Button>
      </div>
    </form>
  );
}
