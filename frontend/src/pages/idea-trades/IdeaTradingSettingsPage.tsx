import { Link } from '@tanstack/react-router';

import { SettingsPanel } from '@/modules/idea-trades';

/** The settings of trading by ideas, apart from the journal, which they lead back to. */
export function IdeaTradingSettingsPage() {
  return (
    <section className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold">Торговля идеями</h1>
        <Link to="/idea-trades" className="text-sm text-run hover:underline">
          Журнал
        </Link>
      </header>
      <SettingsPanel />
    </section>
  );
}
