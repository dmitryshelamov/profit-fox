import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { DailyStopBanner, guardsQuery } from '@/modules/idea-autos';
import { SettingsPanel } from '@/modules/idea-trades';

/** The settings of trading by ideas, apart from the screen «Сделки», which they lead back to. */
export function IdeaTradingSettingsPage() {
  const guards = useQuery(guardsQuery());

  return (
    <section className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Торговля идеями</h1>
        <p className="text-sm text-muted">
          Аккаунт и сумма по умолчанию для ручных входов и защиты денег. Защиты считают только
          реальные сделки; учебные сделки автоматов в них не входят.
        </p>
        <Link to="/idea-trades" className="text-sm text-run hover:underline">
          Сделки
        </Link>
      </header>
      <DailyStopBanner />
      <SettingsPanel guards={guards.data} />
    </section>
  );
}
