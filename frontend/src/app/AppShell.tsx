import { Link, Outlet } from '@tanstack/react-router';

import type { Run } from '@/api/runs';
import { useActiveRuns } from '@/pages/runs/useActiveRuns';

const sections = [
  { to: '/signals', title: 'Сигналы' },
  { to: '/strategies', title: 'Стратегии' },
  { to: '/runs', title: 'Прогоны' },
  { to: '/ideas', title: 'Идеи' },
  { to: '/trades', title: 'Сделки' },
  { to: '/risk', title: 'Риск' },
  { to: '/market', title: 'Рынок' },
  { to: '/scoring', title: 'Скоринг' },
  { to: '/accounts', title: 'Аккаунты' },
] as const;

/**
 * The header of the lists, of «Прогоны» and of the page of a run (spec bench-ui «Общая шапка»): the sign and the name
 * of the product, the nine sections with the current one marked, and on the right the state of the run queue. The
 * editors are not under it: they take the whole screen.
 */
export function AppShell() {
  return (
    <div className="flex min-h-full flex-col">
      <header className="sticky top-0 z-20 flex h-12 shrink-0 items-stretch gap-12 border-b border-line bg-panel px-4">
        <div className="flex items-center gap-2 text-[15px] font-bold">
          <span
            aria-hidden="true"
            className="grid size-6 place-items-center rounded-md bg-accent text-[10px] font-extrabold text-on-accent"
          >
            PF
          </span>
          profit-fox
        </div>
        <nav aria-label="Разделы" className="flex items-stretch gap-7">
          {sections.map((section) => (
            <Link
              key={section.to}
              to={section.to}
              className="flex items-center border-b-2 pt-0.5 text-[13px]"
              activeProps={{ className: 'border-accent font-semibold text-text' }}
              inactiveProps={{ className: 'border-transparent text-muted hover:text-text' }}
            >
              {section.title}
            </Link>
          ))}
        </nav>
        <QueueState />
      </header>
      <Outlet />
    </div>
  );
}

/** How many runs go and wait, in the words of the header. */
export function queueWords(runs: readonly Run[]): string {
  const going = runs.filter((run) => run.state === 'running').length;
  const waiting = runs.length - going;
  return going > 0 && waiting > 0
    ? `Идёт ${going} · ждёт ${waiting}`
    : going > 0
      ? `Идёт ${going}`
      : waiting > 0
        ? `Ждёт ${waiting}`
        : 'Очередь пуста';
}

/**
 * The state of the run queue (spec bench-ui «Состояние очереди в шапке»): how many runs go and wait, or that the queue
 * is empty, or that the API did not answer. A click opens «Прогоны».
 */
function QueueState() {
  const active = useActiveRuns();
  const failed = active.isError;
  const going = !failed && active.data?.some((run) => run.state === 'running') === true;

  return (
    <Link
      to="/runs"
      title="Очередь прогонов"
      data-state={failed ? 'error' : active.data ? 'ready' : 'loading'}
      className={`ml-auto flex items-center gap-2 text-xs hover:text-text ${failed ? 'text-err' : 'text-muted'}`}
    >
      <span
        aria-hidden="true"
        className={`size-1.5 rounded-full ${failed ? 'bg-err' : going ? 'bg-run' : 'bg-faint'}`}
      />
      {failed ? 'Стенд не отвечает' : active.data ? queueWords(active.data) : 'Загрузка…'}
    </Link>
  );
}
