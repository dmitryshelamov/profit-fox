import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { accountsQuery, exchangeName } from '@/modules/exchange-accounts';

const cell = 'px-5 py-4';

export function AccountsPage() {
  const accounts = useQuery(accountsQuery);

  return (
    <section className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Аккаунты</h1>
          <p className="text-sm text-muted">
            Ключи Bybit и токены T-Invest, от имени которых будут торговать боты. Secret и токен
            хранятся зашифрованными и не показываются.
          </p>
        </div>
        <Link
          to="/accounts/new"
          className="inline-flex h-8 items-center rounded-md bg-accent px-3 text-sm font-bold text-on-accent hover:brightness-110"
        >
          + Новый аккаунт
        </Link>
      </header>
      {accounts.isError && <p role="alert">Не удалось загрузить аккаунты.</p>}
      {accounts.data?.length === 0 && (
        <p className="rounded-xl border border-line bg-panel px-5 py-4 text-sm text-muted">
          Аккаунтов пока нет.{' '}
          <Link to="/accounts/new" className="font-semibold text-accent hover:underline">
            Добавить аккаунт
          </Link>
        </p>
      )}
      {accounts.data && accounts.data.length > 0 && (
        <table className="w-full border-separate border-spacing-y-2 text-sm">
          <thead className="text-left text-[11px] tracking-wide text-faint uppercase">
            <tr>
              <th className={cell}>Биржа</th>
              <th className={cell}>Название</th>
              <th className={cell}>Ключ / счёт</th>
              <th className={cell}>UID</th>
              <th className={cell}>Торгует</th>
              <th className={cell} />
            </tr>
          </thead>
          <tbody>
            {accounts.data.map((account) => (
              <tr key={account.id} className="bg-panel">
                <td className={cell}>{exchangeName(account.exchange)}</td>
                <td className={`${cell} font-semibold`}>{account.name}</td>
                <td className={`${cell} font-mono text-muted`}>
                  {account.exchange === 'tinvest' ? account.accountId : account.apiKey}
                </td>
                <td className={`${cell} font-mono`}>{account.uid}</td>
                <td className={`${cell} font-mono`}>{account.categories.join(', ')}</td>
                <td className={`${cell} text-right whitespace-nowrap`}>
                  <Link
                    to="/accounts/$accountId"
                    params={{ accountId: account.id }}
                    className="font-semibold text-accent hover:underline"
                    aria-label={`Открыть: ${account.name}`}
                  >
                    Открыть →
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
