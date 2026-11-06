import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { AutoForm, autoQuery, formOfAuto, formOfDefaults } from '@/modules/idea-autos';
import { tradingSettingsQuery } from '@/modules/idea-trades';

/** The form of a new auto (`id` is none) or of a saved one: the same fields, the defaults of a new one from the settings of trading. */
export function IdeaAutoPage({ id }: { id: string | null }) {
  const navigate = useNavigate();
  const settings = useQuery(tradingSettingsQuery());
  const auto = useQuery({ ...autoQuery(id ?? ''), enabled: id !== null });
  const toList = () => void navigate({ to: '/idea-trades/autos' });
  const initial =
    id === null
      ? settings.data && formOfDefaults(settings.data)
      : auto.data && formOfAuto(auto.data);
  const title = id === null ? 'Новый автомат' : `Автомат «${auto.data?.name ?? ''}»`;

  return (
    <section className="space-y-4">
      <nav aria-label="Путь" className="text-sm text-muted">
        <Link to="/idea-trades" className="hover:underline">
          Сделки
        </Link>{' '}
        /{' '}
        <Link to="/idea-trades/autos" className="hover:underline">
          Автоматы
        </Link>{' '}
        / <span className="text-faint">{id === null ? 'Новый' : (auto.data?.name ?? '')}</span>
      </nav>
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">{title}</h1>
        <p className="text-sm text-muted">
          Условия, аккаунт, сумма и режим автомата. Новые условия действуют на идеи, родившиеся
          после «Сохранить»; открытые сделки автомата идут как шли.
        </p>
      </header>
      {(settings.isError || auto.isError) && <p role="alert">Не удалось загрузить автомат.</p>}
      {initial && (
        <AutoForm
          key={id ?? 'new'}
          id={id}
          initial={initial}
          maxAmount={settings.data?.maxAmount}
          onSaved={toList}
          onCancel={toList}
        />
      )}
    </section>
  );
}
