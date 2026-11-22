import type { Issue } from '../model/issues';
import { Card } from './Card';

/**
 * Every error of the last check as a row: the node and its id, the place, the text, and «Показать на холсте», which
 * selects the node of the error.
 */
export function ErrorsCard({
  issues,
  failed,
  titleOf,
  onSelect,
  onClose,
}: {
  readonly issues: readonly Issue[];
  readonly failed: boolean;
  /** The title of a node by its id, as the canvas shows it. */
  readonly titleOf: (node: string) => string;
  readonly onSelect: (node: string) => void;
  readonly onClose: () => void;
}) {
  return (
    <Card title="Ошибки" heading="Ошибки графа" tone="error" onClose={onClose}>
      {failed && <p className="text-err">Проверка не удалась: API стенда не ответил.</p>}
      {issues.length === 0 && !failed && <p className="text-muted">Ошибок нет.</p>}
      <ul className="flex flex-col gap-2">
        {issues.map((issue, index) => (
          <li key={index} className="flex gap-2.5 rounded-lg bg-err-bg px-3 py-2.5">
            <span aria-hidden="true" className="mt-1.5 size-2 shrink-0 rounded-full bg-err" />
            <span className="flex min-w-0 flex-col gap-1">
              {issue.node !== undefined && (
                <span className="flex items-baseline gap-2">
                  <span className="font-semibold">{titleOf(issue.node)}</span>
                  <span className="font-mono text-[11px] text-muted">{issue.node}</span>
                </span>
              )}
              <span className="font-mono text-[11px] text-err">{issue.place}</span>
              <span className="text-xs">
                <span className="text-muted">{issue.code} · </span>
                {issue.reason}
              </span>
              {issue.node !== undefined && (
                <button
                  type="button"
                  className="cursor-pointer self-start text-xs font-semibold text-accent hover:underline"
                  onClick={() => onSelect(issue.node!)}
                >
                  Показать на холсте
                  <span aria-hidden="true"> →</span>
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>
    </Card>
  );
}
