import { groupDigits } from '@/shared/format/format';
import { pageSizes } from '@/shared/paging/pageSizes';

const button =
  'h-8 w-8 rounded-md border border-line bg-panel text-muted hover:bg-panel-2 disabled:opacity-40 disabled:hover:bg-panel';

/** Turns the pages of a list that has `total` rows: the buttons, the page of all, the rows shown and the size of a page. */
export function Pager({
  page,
  pageSize,
  total,
  onPage,
  onPageSize,
}: {
  /** The number of the page from 1. */
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
  onPageSize: (size: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);

  return (
    <div className="flex flex-wrap items-center gap-3 text-sm">
      <button
        type="button"
        aria-label="Предыдущая страница"
        disabled={page <= 1}
        onClick={() => onPage(page - 1)}
        className={button}
      >
        ‹
      </button>
      <span className="text-muted">{`${page} из ${pages}`}</span>
      <button
        type="button"
        aria-label="Следующая страница"
        disabled={page >= pages}
        onClick={() => onPage(page + 1)}
        className={button}
      >
        ›
      </button>
      <span className="text-muted">
        {`${groupDigits(String(from))}–${groupDigits(String(to))} из ${groupDigits(String(total))}`}
      </span>
      <label className="ml-auto flex items-center gap-2 text-muted">
        На странице
        <select
          aria-label="На странице"
          value={pageSize}
          onChange={(event) => onPageSize(Number(event.target.value))}
          className="h-8 rounded-md border border-line bg-panel px-2 text-text"
        >
          {pageSizes.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
