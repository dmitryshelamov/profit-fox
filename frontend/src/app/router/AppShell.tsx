import { Link, useMatchRoute } from '@tanstack/react-router';
import type { ReactNode } from 'react';

import { ComparisonBar, useComparison } from '@/modules/comparison';

const navLink = 'border-b-2 border-transparent py-3 text-muted hover:text-text';

export function AppShell({ children }: { children: ReactNode }) {
  const barShown = useComparison().length > 0;
  // The market table has fourteen columns and the ideas table fifteen: they take the width of the mockup, 1376 px.
  const match = useMatchRoute();
  const wide =
    match({ to: '/market' }) !== false ||
    match({ to: '/ideas' }) !== false ||
    match({ to: '/market/$category/$symbol' }) !== false;

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex items-center gap-8 border-b border-line bg-panel px-4">
        <Link to="/strategies" className="flex items-center gap-2 font-bold">
          <span className="grid size-6 place-items-center rounded-md bg-accent text-[11px] font-extrabold text-on-accent">
            PF
          </span>
          profit-fox
        </Link>
        <nav aria-label="Разделы" className="flex gap-6 text-sm">
          <Link
            to="/strategies"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Стратегии
          </Link>
          <Link
            to="/nodes"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Составные узлы
          </Link>
          <Link
            to="/market"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Рынок
          </Link>
          <Link
            to="/ideas"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Идеи
          </Link>
          <Link
            to="/bots"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Боты
          </Link>
          <Link
            to="/accounts"
            className={navLink}
            activeProps={{ className: 'border-accent! text-text!', 'aria-current': 'page' }}
          >
            Аккаунты
          </Link>
        </nav>
      </header>
      <main
        className={`mx-auto w-full ${wide ? 'max-w-[1376px]' : 'max-w-[1100px]'} flex-1 px-4 pt-9 ${barShown ? 'pb-28' : 'pb-9'}`}
      >
        {children}
      </main>
      <ComparisonBar />
    </div>
  );
}
