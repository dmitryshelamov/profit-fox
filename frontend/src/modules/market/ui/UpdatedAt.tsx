import { ageText, isStale } from '../model/snapshotAge';

/** «Обновлено 14 с назад» with a green dot, red once the data is stale. */
export function UpdatedAt({ at, now }: { at: string; now: number }) {
  const stale = isStale(at, now);

  return (
    <p className="flex items-center gap-2 text-sm">
      <span aria-hidden="true" className={`size-2 rounded-full ${stale ? 'bg-err' : 'bg-ok'}`} />
      <span className={stale ? 'font-semibold text-err' : 'text-muted'}>
        Обновлено {ageText(at, now)}
      </span>
    </p>
  );
}
