import type { Report } from '../editing/useGraphEditing';
import type { Issue } from '../model/issues';
import { Card } from './Card';

/** The errors of the check: graph errors with a way to their node, and usages or bots a save would break. */
export function IssuesCard({
  issues,
  report,
  onShow,
  onClose,
}: {
  issues: readonly Issue[];
  report: Report;
  onShow: (nodeId: string) => void;
  onClose: () => void;
}) {
  const count = issues.length + report.usages.length + report.bots.length;

  return (
    <Card
      label="Ошибки"
      title={`Ошибки · ${count}`}
      tone={count > 0 ? 'err' : 'line'}
      onClose={onClose}
    >
      {count === 0 && <p className="text-sm text-muted">Ошибок нет.</p>}
      {issues.map((issue) => (
        <div
          key={`${issue.key}:${issue.message}`}
          className="space-y-1 rounded-md bg-err-bg p-2.5 text-xs"
        >
          <p className="font-mono">{issue.key}</p>
          <p className="text-muted">{issue.message}</p>
          {issue.nodeId && (
            <button type="button" onClick={() => onShow(issue.nodeId!)} className="text-accent">
              Показать на холсте
            </button>
          )}
        </div>
      ))}
      {report.usages.map((usage) => (
        <Broken
          key={usage.id}
          what={`${usage.kind === 'strategy' ? 'Стратегия' : 'Узел'} «${usage.name}»`}
          errors={usage.errors}
        />
      ))}
      {report.bots.map((bot) => (
        <Broken key={bot.id} what={`Бот «${bot.name}»`} errors={bot.errors} />
      ))}
      <p className="text-[11.5px] text-faint">
        Проверка идёт на сервере вскоре после каждой правки.
      </p>
    </Card>
  );
}

function Broken({ what, errors }: { what: string; errors: Readonly<Record<string, string>> }) {
  return (
    <div className="space-y-1 rounded-md bg-err-bg p-2.5 text-xs">
      <p className="font-semibold">Сломается: {what}</p>
      {Object.entries(errors).map(([key, message]) => (
        <p key={key} className="text-muted">
          <span className="font-mono text-text">{key}</span> {message}
        </p>
      ))}
    </div>
  );
}
