import type { Issue } from '../model/issues';
import { buttonClass, Card } from './Card';

/** Every error of the last check with its place; a click on one with a node selects that node. */
export function ErrorsCard({
  issues,
  failed,
  onSelect,
  onClose,
}: {
  readonly issues: readonly Issue[];
  readonly failed: boolean;
  readonly onSelect: (node: string) => void;
  readonly onClose: () => void;
}) {
  return (
    <Card title="Ошибки" onClose={onClose}>
      {failed && <p className="text-red-300">Проверка не удалась: API стенда не ответил.</p>}
      {issues.length === 0 && !failed && <p className="text-zinc-400">Ошибок нет.</p>}
      <ul className="space-y-2">
        {issues.map((issue, index) => (
          <li
            key={index}
            className="flex flex-col gap-1 rounded border border-red-900 bg-red-950/40 p-2"
          >
            <span className="font-mono text-xs text-red-300">{issue.place}</span>
            <span className="text-xs">
              <span className="text-zinc-400">{issue.code} · </span>
              {issue.reason}
            </span>
            {issue.node !== undefined && (
              <button type="button" className={buttonClass} onClick={() => onSelect(issue.node!)}>
                Показать на холсте
              </button>
            )}
          </li>
        ))}
      </ul>
    </Card>
  );
}
