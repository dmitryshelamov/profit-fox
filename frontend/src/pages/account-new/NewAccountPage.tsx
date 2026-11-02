import { Link } from '@tanstack/react-router';

import { AccountForm } from '@/modules/exchange-accounts';

export function NewAccountPage() {
  return (
    <section className="space-y-6">
      <header className="space-y-2">
        <p className="text-xs text-muted">
          <Link to="/accounts" className="hover:underline">
            Аккаунты
          </Link>{' '}
          / Новый аккаунт
        </p>
        <h1 className="text-2xl font-bold">Новый аккаунт</h1>
      </header>
      <AccountForm />
    </section>
  );
}
