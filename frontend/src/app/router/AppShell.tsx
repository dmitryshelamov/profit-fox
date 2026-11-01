import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';

const navLink = 'border-b-2 border-transparent py-3 text-muted hover:text-text';

export function AppShell({ children }: { children: ReactNode }) {
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
        </nav>
      </header>
      <main className="mx-auto w-full max-w-[1100px] flex-1 px-4 py-9">{children}</main>
    </div>
  );
}
