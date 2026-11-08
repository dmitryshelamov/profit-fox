import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { ApiError } from '@/shared/api/client';
import type { FieldErrors } from '@/shared/api/problems';
import { Button } from '@/shared/ui/Button';

import { type BrokerAccount, createAccount, listBrokerAccounts } from '../api';
import { accountFieldErrors, accountProblem } from '../model/accountProblems';
import {
  brokerAccountProblem,
  brokerAccountStatus,
  brokerAccountType,
} from '../model/brokerAccounts';

const exchanges = [
  { value: 'bybit', label: 'Bybit' },
  { value: 'tinvest', label: 'T-Invest' },
] as const;
const input =
  'block h-9 w-full rounded-md border bg-app px-3 text-sm text-text placeholder:text-faint';
const labelText = 'text-[13px] text-muted';

/**
 * The form of a new account: Bybit takes a key and a secret, T-Invest a token and one of the accounts it opens. The secret and
 * the token are never kept after a refusal, and the accounts read with a token go with it.
 */
export function AccountForm() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [exchange, setExchange] = useState<'bybit' | 'tinvest'>('bybit');
  const [name, setName] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [apiSecret, setApiSecret] = useState('');
  const [token, setToken] = useState('');
  const [accounts, setAccounts] = useState<BrokerAccount[]>();
  const [accountId, setAccountId] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({ fields: {}, form: [] });
  const [saving, setSaving] = useState(false);
  const [listing, setListing] = useState(false);
  const fieldError = (key: string) => errors.fields[key]?.join(' ');

  function fail(error: unknown) {
    setApiSecret('');
    setToken('');
    setAccounts(undefined);
    setAccountId('');
    const laidOut = error instanceof ApiError ? accountFieldErrors(error.errors) : undefined;

    setErrors(
      laidOut && (Object.keys(laidOut.fields).length > 0 || laidOut.form.length > 0)
        ? laidOut
        : { fields: {}, form: [accountProblem(error)] },
    );
  }

  function switchExchange(value: 'bybit' | 'tinvest') {
    setExchange(value);
    setErrors({ fields: {}, form: [] });
    setApiSecret('');
    setToken('');
    setAccounts(undefined);
    setAccountId('');
  }

  async function showAccounts() {
    setErrors({ fields: {}, form: [] });
    setListing(true);

    try {
      setAccounts(await listBrokerAccounts(token));
      setAccountId('');
    } catch (error) {
      fail(error);
    } finally {
      setListing(false);
    }
  }

  async function save() {
    setErrors({ fields: {}, form: [] });
    setSaving(true);

    try {
      const id = await createAccount(
        exchange === 'bybit'
          ? { name, exchange, apiKey, apiSecret }
          : { name, exchange, token, accountId },
      );

      await queryClient.invalidateQueries({ queryKey: ['exchange-accounts'] });
      await navigate({ to: '/accounts/$accountId', params: { accountId: id } });
    } catch (error) {
      fail(error);
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
        <span className={labelText}>Биржа</span>
        <div className="mt-1 grid grid-cols-2 gap-1 rounded-md border border-line bg-app p-1">
          {exchanges.map((item) => (
            <button
              key={item.value}
              type="button"
              aria-pressed={exchange === item.value}
              className={`h-7 rounded text-sm ${exchange === item.value ? 'bg-panel-2 font-bold' : 'text-muted'}`}
              onClick={() => switchExchange(item.value)}
            >
              {item.label}
            </button>
          ))}
        </div>
        {problem('exchange')}
      </div>
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
      {exchange === 'bybit' ? (
        <>
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
        </>
      ) : (
        <>
          <div>
            <label htmlFor="account-token" className={labelText}>
              Токен
            </label>
            <div className="flex gap-2">
              <input
                id="account-token"
                type="password"
                autoComplete="off"
                className={`${input} font-mono ${border('token')}`}
                value={token}
                onChange={(event) => setToken(event.target.value)}
              />
              <Button type="button" onClick={() => void showAccounts()} disabled={listing}>
                {listing ? 'Читаем…' : 'Показать счета'}
              </Button>
            </div>
            {problem('token')}
          </div>
          {accounts && (
            <fieldset>
              <legend className={labelText}>Счёт</legend>
              {accounts.length === 0 && <p className="text-sm text-muted">У токена нет счетов.</p>}
              <ul className="mt-1 space-y-1">
                {accounts.map((account) => {
                  const reason = brokerAccountProblem(account.problem);

                  return (
                    <li
                      key={account.id}
                      className={`flex items-center gap-3 rounded-md border px-3 py-2 text-sm ${accountId === account.id ? 'border-accent' : 'border-line'}`}
                    >
                      <input
                        id={`broker-account-${account.id}`}
                        type="radio"
                        name="broker-account"
                        value={account.id}
                        checked={accountId === account.id}
                        disabled={reason !== null}
                        onChange={() => setAccountId(account.id)}
                      />
                      <label htmlFor={`broker-account-${account.id}`} className="flex-1">
                        <span className="font-mono font-semibold">{account.id}</span>{' '}
                        <span className="text-muted">{account.name}</span>
                        <span className="block text-xs text-faint">
                          {brokerAccountType(account.type)}, {brokerAccountStatus(account.status)}
                        </span>
                      </label>
                      {reason && <span className="text-xs text-err">{reason}</span>}
                    </li>
                  );
                })}
              </ul>
            </fieldset>
          )}
          {problem('accountId')}
          <p className="text-xs text-faint">
            Токен Invest API с полным доступом. T-Invest проверит счёт при сохранении; токен
            хранится зашифрованным и больше не показывается.
          </p>
        </>
      )}
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
        <Button
          look="primary"
          type="submit"
          disabled={saving || (exchange === 'tinvest' && accountId === '')}
        >
          {saving
            ? exchange === 'bybit'
              ? 'Проверяем ключ…'
              : 'Проверяем счёт…'
            : 'Добавить аккаунт'}
        </Button>
      </div>
    </form>
  );
}
