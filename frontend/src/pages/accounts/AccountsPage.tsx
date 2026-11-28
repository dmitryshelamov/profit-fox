import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import {
  type Account,
  accountBalance,
  accountsKey,
  addAccount,
  type Balance,
  deleteAccount,
  freeText,
  listAccounts,
  marginModeText,
  renameAccount,
} from '@/api/accounts';
import type { RequestError } from '@/api/client';
import { localMoment, zoneLabel } from '@/shared/time';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { Menu } from '@/shared/ui/Menu';
import { Page, PageTitle } from '@/shared/ui/Page';
import { Table, Td, Th, Tr } from '@/shared/ui/Table';
import { TextField } from '@/shared/ui/TextField';

/** The only set of permissions the bench takes (design.md, decision 8): the table writes it as words. */
const permissions = 'торговля · бессрочные';

/** What a dialog of the page is open for. */
type Asked =
  | { readonly kind: 'add' }
  | { readonly kind: 'rename'; readonly account: Account }
  | { readonly kind: 'delete'; readonly account: Account };

/**
 * «Аккаунты» (change add-exchange-accounts, spec exchange-accounts «Раздел «Аккаунты»»): the accounts of Bybit with the
 * permissions of their keys, the margin mode kept at the addition or at the last «Показать» (the list does not call
 * Bybit), the free money of each by its button, the menu of a row with a new name and the removal,
 * and the dialog «Новый аккаунт». The secret never comes back from the server; a balance lives on the page until it is
 * left.
 */
export function AccountsPage() {
  const client = useQueryClient();
  const accounts = useQuery({ queryKey: accountsKey, queryFn: listAccounts });
  const [asked, setAsked] = useState<Asked | null>(null);
  const [balances, setBalances] = useState<Readonly<Record<string, Balance | 'loading'>>>({});
  const rows = accounts.data ?? [];

  // The margin mode is read again with the balance and kept by the server: the row shows the one read now.
  const showBalance = async (account: Account) => {
    setBalances((old) => ({ ...old, [account.id]: 'loading' }));
    let balance: Balance;
    try {
      balance = await accountBalance(account.id);
    } catch {
      balance = { ok: false, reason: 'Стенд не ответил' };
    }

    setBalances((old) => ({ ...old, [account.id]: balance }));
    if (balance.ok) {
      const marginMode = balance.marginMode;
      client.setQueryData<Account[]>(accountsKey, (old) =>
        old?.map((row) => (row.id === account.id ? { ...row, marginMode } : row)),
      );
    }
  };

  return (
    <Page>
      <PageTitle
        title="Аккаунты"
        hint="Ключи Bybit для сделок по идеям. Секрет хранится зашифрованным и после сохранения не показывается."
      >
        {rows.length > 0 && (
          <Button variant="primary" onClick={() => setAsked({ kind: 'add' })}>
            + Добавить аккаунт
          </Button>
        )}
      </PageTitle>

      {accounts.isPending && <p className="text-muted">Загрузка…</p>}
      {accounts.isError && (
        <p role="alert" className="text-err">
          {accounts.error.message}
        </p>
      )}
      {accounts.data?.length === 0 && (
        <section aria-label="Аккаунтов нет" className="flex flex-col items-start gap-3">
          <p className="text-muted">
            Аккаунтов нет. Аккаунт Bybit нужен для сделок по идеям: добавьте ключ API с правом
            торговли бессрочными контрактами.
          </p>
          <Button variant="primary" onClick={() => setAsked({ kind: 'add' })}>
            + Добавить аккаунт
          </Button>
        </section>
      )}
      {rows.length > 0 && (
        <section className="flex flex-col gap-3">
          <Table label="Аккаунты">
            <thead>
              <tr>
                <Th className="w-[210px]">Имя</Th>
                <Th className="w-[140px]">UID</Th>
                <Th className="w-[250px]">Ключ API</Th>
                <Th className="w-[200px]">Права ключа</Th>
                <Th className="w-[140px]">Режим маржи</Th>
                <Th className="w-[170px]">Свободно, USDT</Th>
                <Th>Добавлен, {zoneLabel()}</Th>
                <Th className="w-px">
                  <span className="sr-only">Действия</span>
                </Th>
              </tr>
            </thead>
            <tbody>
              {rows.map((account) => (
                <Tr key={account.id} aria-label={`Аккаунт ${account.name}`} className="h-[52px]">
                  <Td className="font-semibold">{account.name}</Td>
                  <Td className="font-mono text-muted">{String(account.uid)}</Td>
                  <Td className="font-mono">{account.apiKey}</Td>
                  <Td className="text-muted">{permissions}</Td>
                  <Td className="text-muted">{marginModeText(account.marginMode)}</Td>
                  <Td>
                    <BalanceCell
                      account={account}
                      balance={balances[account.id]}
                      onShow={() => void showBalance(account)}
                    />
                  </Td>
                  <Td className="font-mono text-muted">
                    {localMoment(Date.parse(account.createdAt))}
                  </Td>
                  <Td>
                    <span className="flex justify-end">
                      <Menu
                        label={`Действия ${account.name}`}
                        items={[
                          {
                            label: 'Переименовать',
                            onSelect: () => setAsked({ kind: 'rename', account }),
                          },
                          {
                            label: 'Удалить',
                            danger: true,
                            onSelect: () => setAsked({ kind: 'delete', account }),
                          },
                        ]}
                      />
                    </span>
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          <p className="text-xs text-muted">Баланс читается у Bybit по кнопке и не хранится.</p>
        </section>
      )}

      {asked?.kind === 'add' && <AddDialog onClose={() => setAsked(null)} />}
      {asked?.kind === 'rename' && (
        <RenameDialog account={asked.account} onClose={() => setAsked(null)} />
      )}
      {asked?.kind === 'delete' && (
        <DeleteDialog account={asked.account} onClose={() => setAsked(null)} />
      )}
    </Page>
  );
}

/**
 * The free money of a row (spec exchange-accounts «Баланс в строке»): «Показать» until it is asked, then the number with
 * a button that reads it again, or the refusal in red.
 */
function BalanceCell({
  account,
  balance,
  onShow,
}: {
  readonly account: Account;
  readonly balance: Balance | 'loading' | undefined;
  readonly onShow: () => void;
}) {
  if (balance === undefined) {
    return (
      <Button aria-label={`Показать баланс ${account.name}`} onClick={onShow}>
        Показать
      </Button>
    );
  }

  if (balance === 'loading') {
    return <span className="text-muted">Читаем у Bybit…</span>;
  }

  return (
    <span className="flex items-center gap-2">
      {balance.ok ? (
        <span className="font-mono" data-testid={`free-${account.id}`}>
          {freeText(balance.free)}
        </span>
      ) : (
        <span role="alert" className="text-xs text-err">
          {balance.reason}
        </span>
      )}
      <button
        type="button"
        aria-label={`Обновить баланс ${account.name}`}
        title="Прочитать у Bybit ещё раз"
        className="cursor-pointer rounded px-1 text-muted hover:text-text"
        onClick={onShow}
      >
        ↻
      </button>
    </span>
  );
}

// The errors of a field, or of none: a key refused by Bybit or by the rules names what to do with it.
function errorsAt(errors: readonly RequestError[], field: string | null): string[] {
  return errors
    .filter((error) => (field === null ? !error.field : error.field === field))
    .map((error) =>
      error.code === 'KeyRejected' ? `Bybit не принял ключ: ${error.reason}` : error.reason,
    );
}

function FieldError({ id, reasons }: { readonly id: string; readonly reasons: readonly string[] }) {
  return reasons.length === 0 ? null : (
    <span id={id} role="alert" className="text-xs text-err">
      {reasons.join('; ')}
    </span>
  );
}

/**
 * «Новый аккаунт» (spec exchange-accounts «Отказ в диалоге», «Идёт проверка»): the name, the API key and the secret,
 * hidden as it is typed. While Bybit checks the key the button waits; any refusal of the server keeps the dialog open,
 * tells why under its field and empties the secret.
 */
function AddDialog({ onClose }: { readonly onClose: () => void }) {
  const client = useQueryClient();
  const [name, setName] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [secret, setSecret] = useState('');
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const add = useMutation({
    mutationFn: () => addAccount(name, apiKey, secret),
    onSuccess: (result) => {
      if (result.ok) {
        client.setQueryData<Account[]>(accountsKey, (old) => [...(old ?? []), result.value]);
        void client.invalidateQueries({ queryKey: accountsKey });
        onClose();
      } else {
        setErrors(result.errors);
        setSecret('');
      }
    },
    onError: (failure) => {
      setErrors([{ code: 'Failed', reason: failure.message }]);
      setSecret('');
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setErrors([]);
    add.mutate();
  };

  const nameErrors = errorsAt(errors, 'name');
  const keyErrors = errorsAt(errors, 'apiKey');
  const secretErrors = errorsAt(errors, 'secret');
  const general = errorsAt(errors, null);

  return (
    <Dialog
      label="Новый аккаунт"
      className="w-[520px]"
      onClose={add.isPending ? undefined : onClose}
    >
      <div className="flex items-center justify-between">
        <h2 className="text-base font-bold">Новый аккаунт</h2>
        <button
          type="button"
          aria-label="Закрыть"
          disabled={add.isPending}
          className="cursor-pointer text-lg leading-none text-muted hover:text-text disabled:cursor-not-allowed"
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <form className="flex flex-col gap-4" onSubmit={submit}>
        <label className="flex flex-col gap-1.5 text-xs text-muted">
          Имя
          <TextField
            aria-label="Имя"
            autoFocus
            maxLength={60}
            value={name}
            aria-invalid={nameErrors.length > 0}
            aria-describedby={nameErrors.length > 0 ? 'account-name-error' : undefined}
            className={nameErrors.length > 0 ? 'border-err' : ''}
            onChange={(event) => setName(event.target.value)}
          />
          <FieldError id="account-name-error" reasons={nameErrors} />
        </label>
        <label className="flex flex-col gap-1.5 text-xs text-muted">
          Ключ API
          <TextField
            aria-label="Ключ API"
            mono
            autoComplete="off"
            spellCheck={false}
            value={apiKey}
            aria-invalid={keyErrors.length > 0}
            aria-describedby={keyErrors.length > 0 ? 'account-key-error' : undefined}
            className={keyErrors.length > 0 ? 'border-err' : ''}
            onChange={(event) => setApiKey(event.target.value)}
          />
          <FieldError id="account-key-error" reasons={keyErrors} />
        </label>
        <label className="flex flex-col gap-1.5 text-xs text-muted">
          Секрет
          <TextField
            aria-label="Секрет"
            type="password"
            mono
            autoComplete="off"
            spellCheck={false}
            value={secret}
            aria-invalid={secretErrors.length > 0}
            aria-describedby={secretErrors.length > 0 ? 'account-secret-error' : undefined}
            className={secretErrors.length > 0 ? 'border-err' : ''}
            onChange={(event) => setSecret(event.target.value)}
          />
          <FieldError id="account-secret-error" reasons={secretErrors} />
        </label>
        <p className="text-xs text-muted">
          Ключ проверяется у Bybit при сохранении. Нужны: единый торговый аккаунт, право торговли
          бессрочными контрактами. Запрещено: право вывода.
        </p>
        {general.length > 0 && (
          <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
            {general.join('; ')}
          </p>
        )}
        <span className="flex justify-end gap-2">
          <Button disabled={add.isPending} onClick={onClose}>
            Отмена
          </Button>
          <Button type="submit" variant="primary" disabled={add.isPending}>
            {add.isPending ? 'Проверяем ключ' : 'Проверить и сохранить'}
          </Button>
        </span>
      </form>
    </Dialog>
  );
}

/** A new name of an account (spec exchange-accounts «Переименование аккаунта»); a refusal is told under the field. */
function RenameDialog({
  account,
  onClose,
}: {
  readonly account: Account;
  readonly onClose: () => void;
}) {
  const client = useQueryClient();
  const [name, setName] = useState(account.name);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const rename = useMutation({
    mutationFn: () => renameAccount(account.id, name),
    onSuccess: (result) => {
      if (result.ok) {
        client.setQueryData<Account[]>(accountsKey, (old) =>
          old?.map((row) => (row.id === result.value.id ? result.value : row)),
        );
        onClose();
      } else {
        setErrors(result.errors);
      }
    },
    onError: (failure) => setErrors([{ code: 'Failed', reason: failure.message }]),
  });
  const reasons = [...errorsAt(errors, 'name'), ...errorsAt(errors, null)];

  return (
    <Dialog
      label={`Переименовать аккаунт ${account.name}`}
      title="Переименовать аккаунт"
      onClose={onClose}
    >
      <form
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          setErrors([]);
          rename.mutate();
        }}
      >
        <label className="flex flex-col gap-1.5 text-xs text-muted">
          Новое имя
          <TextField
            aria-label="Новое имя"
            autoFocus
            maxLength={60}
            value={name}
            aria-invalid={reasons.length > 0}
            className={reasons.length > 0 ? 'border-err' : ''}
            onChange={(event) => setName(event.target.value)}
          />
          <FieldError id="account-rename-error" reasons={reasons} />
        </label>
        <span className="flex justify-end gap-2">
          <Button onClick={onClose}>Отмена</Button>
          <Button type="submit" variant="primary" disabled={name.trim() === '' || rename.isPending}>
            Переименовать
          </Button>
        </span>
      </form>
    </Dialog>
  );
}

/** The question before a removal (spec exchange-accounts «Подтверждение удаления»): it names the account. */
function DeleteDialog({
  account,
  onClose,
}: {
  readonly account: Account;
  readonly onClose: () => void;
}) {
  const client = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const remove = useMutation({
    mutationFn: () => deleteAccount(account.id),
    onSuccess: () => {
      client.setQueryData<Account[]>(accountsKey, (old) =>
        old?.filter((row) => row.id !== account.id),
      );
      onClose();
    },
    onError: (failure) => setError(failure.message),
  });

  return (
    <Dialog label={`Удалить аккаунт ${account.name}`} title="Удалить аккаунт" onClose={onClose}>
      <p>
        Удалить аккаунт «{account.name}»? Стенд забудет ключ и его секрет; сам ключ на Bybit
        останется, его можно удалить там.
      </p>
      {error !== null && (
        <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
          {error}
        </p>
      )}
      <span className="flex justify-end gap-2">
        <Button onClick={onClose}>Отмена</Button>
        <Button
          variant="danger"
          className="border border-err"
          disabled={remove.isPending}
          onClick={() => remove.mutate()}
        >
          Удалить
        </Button>
      </span>
    </Dialog>
  );
}
