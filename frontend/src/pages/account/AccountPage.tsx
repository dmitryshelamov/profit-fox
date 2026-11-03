import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import {
  accountBalanceQuery,
  accountFieldErrors,
  accountProblem,
  accountQuery,
  BalanceTable,
  deleteAccount,
  renameAccount,
} from '@/modules/exchange-accounts';
import { ApiError } from '@/shared/api/client';
import { formatMoment } from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

const input = 'block h-8 rounded-md border bg-app px-3 text-sm text-text';
const term = 'text-[11px] tracking-wide text-faint uppercase';

export function AccountPage({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const account = useQuery(accountQuery(accountId));
  const balance = useQuery(accountBalanceQuery(accountId));
  const [name, setName] = useState<string>();
  const [nameError, setNameError] = useState<string>();
  const [confirming, setConfirming] = useState(false);
  const [failure, setFailure] = useState<string>();

  if (account.isError) {
    return <p role="alert">Не удалось загрузить аккаунт.</p>;
  }

  if (!account.data) {
    return null;
  }

  const data = account.data;

  async function rename(value: string) {
    setNameError(undefined);

    try {
      await renameAccount(accountId, value);
    } catch (error) {
      setNameError(
        (error instanceof ApiError && accountFieldErrors(error.errors).fields.name?.join(' ')) ||
          accountProblem(error),
      );

      return;
    }

    setName(undefined);
    // Bots carry the name of their account too.
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['exchange-accounts'] }),
      queryClient.invalidateQueries({ queryKey: ['bots'] }),
    ]);
  }

  async function remove() {
    setConfirming(false);
    setFailure(undefined);

    try {
      await deleteAccount(accountId);
    } catch (error) {
      setFailure(accountProblem(error));

      return;
    }

    queryClient.removeQueries({ queryKey: ['exchange-accounts', accountId] });
    await queryClient.invalidateQueries({ queryKey: ['exchange-accounts'] });
    await navigate({ to: '/accounts' });
  }

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <p className="text-xs text-muted">
            <Link to="/accounts" className="hover:underline">
              Аккаунты
            </Link>{' '}
            / {data.name}
          </p>
          {name === undefined ? (
            <h1 className="text-2xl font-bold">{data.name}</h1>
          ) : (
            <form
              className="flex items-start gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                void rename(name);
              }}
            >
              <div>
                <label htmlFor="account-rename" className="sr-only">
                  Название
                </label>
                <input
                  id="account-rename"
                  className={`${input} ${nameError ? 'border-err' : 'border-line'}`}
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
                {nameError && <p className="mt-1 text-xs text-err">{nameError}</p>}
              </div>
              <Button look="primary" type="submit">
                Сохранить
              </Button>
              <Button onClick={() => (setName(undefined), setNameError(undefined))}>Отмена</Button>
            </form>
          )}
        </div>
        <div className="flex gap-2">
          {name === undefined && <Button onClick={() => setName(data.name)}>Переименовать</Button>}
          <Button look="danger" onClick={() => setConfirming(true)}>
            Удалить
          </Button>
        </div>
      </header>
      {failure && (
        <p role="alert" className="rounded-md border border-[#5a2a2e] bg-err-bg p-3 text-sm">
          {failure}
        </p>
      )}
      <dl className="grid grid-cols-3 gap-x-8 gap-y-4 rounded-xl border border-line bg-panel px-5 py-4 text-sm">
        <div>
          <dt className={term}>API key</dt>
          <dd className="font-mono break-all">{data.apiKey}</dd>
        </div>
        <div>
          <dt className={term}>API secret</dt>
          <dd className="font-mono" aria-label="Secret скрыт">
            ••••••••
          </dd>
        </div>
        <div>
          <dt className={term}>UID Bybit</dt>
          <dd className="font-mono">{data.uid}</dd>
        </div>
        <div>
          <dt className={term}>Торгует</dt>
          <dd className="font-mono">{data.categories.join(', ')}</dd>
        </div>
        <div>
          <dt className={term}>Добавлен</dt>
          <dd>{formatMoment(data.createdAt)}</dd>
        </div>
      </dl>
      <section aria-label="Боты аккаунта" className="space-y-2">
        <h2 className="font-semibold">Боты</h2>
        {data.bots.length === 0 ? (
          <p className="text-sm text-muted">С этим аккаунтом не связан ни один бот.</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {data.bots.map((bot) => (
              <li key={bot.id}>
                <Link
                  to="/bots/$botId"
                  params={{ botId: bot.id }}
                  className="font-semibold text-accent hover:underline"
                >
                  {bot.name}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section
        aria-label="Баланс аккаунта"
        className="space-y-3 rounded-xl border border-line bg-panel p-5"
      >
        <div className="flex items-center justify-between">
          <h2 className="font-semibold">Баланс</h2>
          <Button onClick={() => void balance.refetch()} disabled={balance.isFetching}>
            {balance.isFetching ? 'Загружаем…' : 'Показать баланс'}
          </Button>
        </div>
        {balance.isError && (
          <p role="alert" className="text-sm text-err">
            {accountProblem(balance.error)}
          </p>
        )}
        {balance.data && !balance.isError && <BalanceTable balance={balance.data} />}
        {!balance.data && !balance.isError && (
          <p className="text-sm text-muted">Баланс читается с Bybit по запросу и не хранится.</p>
        )}
      </section>
      {confirming && (
        <Dialog
          title={`Удалить аккаунт «${data.name}»?`}
          actions={
            <>
              <Button onClick={() => setConfirming(false)}>Отмена</Button>
              <Button look="danger" onClick={() => void remove()}>
                Удалить
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">
            Ключи аккаунта удаляются из системы. На Bybit ключ остаётся — отзовите его там, если он
            больше не нужен.
          </p>
        </Dialog>
      )}
    </section>
  );
}
