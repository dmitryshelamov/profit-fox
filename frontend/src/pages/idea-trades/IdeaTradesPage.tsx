import { useQuery } from '@tanstack/react-query';

import { ideaTradesQuery, SettingsPanel, TradesTable } from '@/modules/idea-trades';

/** The trades by ideas, the newest first, read again every 15 seconds, and the settings of trading by ideas under them. */
export function IdeaTradesPage() {
  const trades = useQuery(ideaTradesQuery());

  return (
    <section className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Сделки</h1>
        <p className="text-sm text-muted">
          Сделки по идеям: вход, стоп и цель стоят на бирже, сделку ведёт торговый Worker. Войти по
          идее можно со страницы пары.
        </p>
      </header>
      {trades.isPending && <p className="text-sm text-muted">Загрузка…</p>}
      {trades.isError && <p role="alert">Не удалось загрузить сделки.</p>}
      {trades.data?.length === 0 && <p className="text-sm text-muted">Сделок по идеям пока нет.</p>}
      {trades.data && trades.data.length > 0 && <TradesTable trades={trades.data} />}
      <SettingsPanel />
    </section>
  );
}
