import { SelectField } from './SelectField';

/** The sizes of a page of a list. */
export const pageSizes = [50, 100, 200] as const;

/** The number of pages of a list, at least one. */
export function pagesOf(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize));
}

/**
 * The pages of a list (spec bench-ui «Страницы списка»): the range of the rows shown of all, the previous and the next
 * page around «2 из 17», and the size of a page.
 */
export function Pager({
  total,
  page,
  pageSize,
  onPage,
  onPageSize,
}: {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly onPage: (page: number) => void;
  readonly onPageSize: (size: number) => void;
}) {
  const pages = pagesOf(total, pageSize);
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  const step =
    'grid size-7 cursor-pointer place-items-center rounded-md border border-line text-muted hover:text-text disabled:cursor-not-allowed disabled:opacity-45';

  return (
    <nav aria-label="Страницы" className="grid grid-cols-3 items-center text-[12px] text-muted">
      <span>
        {from}–{to} из {total}
      </span>
      <span className="flex items-center justify-center gap-4">
        <button
          type="button"
          aria-label="Предыдущая страница"
          className={step}
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
        >
          ‹
        </button>
        <span className="font-mono text-[13px] text-text">
          {page} из {pages}
        </span>
        <button
          type="button"
          aria-label="Следующая страница"
          className={step}
          disabled={page >= pages}
          onClick={() => onPage(page + 1)}
        >
          ›
        </button>
      </span>
      <label className="flex items-center justify-end gap-4">
        На странице
        <SelectField
          mono
          className="w-[76px]"
          value={pageSize}
          onChange={(event) => onPageSize(Number(event.target.value))}
        >
          {pageSizes.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </SelectField>
      </label>
    </nav>
  );
}
