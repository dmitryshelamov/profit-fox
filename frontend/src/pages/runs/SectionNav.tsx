import { Link } from '@tanstack/react-router';

const linkClass = 'text-zinc-400 hover:text-zinc-100';

/** The sections of the bench: «Сигналы», «Стратегии» and «Прогоны». */
export function SectionNav() {
  return (
    <nav aria-label="Разделы" className="flex gap-4 text-sm">
      <Link to="/signals" className={linkClass} activeProps={{ className: 'text-amber-300' }}>
        Сигналы
      </Link>
      <Link to="/strategies" className={linkClass} activeProps={{ className: 'text-amber-300' }}>
        Стратегии
      </Link>
      <Link to="/runs" className={linkClass} activeProps={{ className: 'text-amber-300' }}>
        Прогоны
      </Link>
    </nav>
  );
}
