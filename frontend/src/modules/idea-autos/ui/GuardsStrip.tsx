import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { guardsQuery } from '../api';
import { guardParts } from '../model/totals';

/** The state of the guards of the money, shared by all the autos: how many trades are open, the risk, the day and the sides. */
export function GuardsStrip() {
  const guards = useQuery(guardsQuery());

  return (
    <section
      aria-label="Защиты"
      className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-xl bg-panel px-4 py-3 text-sm"
    >
      <span className="font-semibold">Защиты для денег, общие для всех автоматов:</span>
      {guards.isError && <span role="alert">Не удалось загрузить защиты.</span>}
      {guards.data && (
        <span className="text-muted">
          {guardParts(guards.data).map((part, index) => (
            <span key={part.name}>
              {index > 0 && <span className="text-faint">{' · '}</span>}
              {part.name} <span className="font-mono text-text">{part.value}</span>
            </span>
          ))}
        </span>
      )}
      <Link to="/idea-trades/settings" className="ml-auto text-run hover:underline">
        Торговля идеями →
      </Link>
    </section>
  );
}
