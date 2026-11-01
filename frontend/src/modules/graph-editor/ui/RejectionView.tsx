import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import type { Rejection } from '../editing/useGraphEditing';

/**
 * A rejected save: usages or bots it would break in a dialog, a change of the library during the save or another
 * failure in a notice. The edit stays in the editor either way.
 */
export function RejectionView({
  rejection,
  onClose,
}: {
  rejection: Rejection;
  onClose: () => void;
}) {
  const broken = [
    ...rejection.usages.map((usage) => ({
      id: usage.id,
      title: usage.name,
      kind: usage.kind === 'strategy' ? 'стратегия' : 'составной узел',
      errors: usage.errors,
    })),
    ...rejection.bots.map((bot) => ({
      id: bot.id,
      title: bot.name,
      kind: 'бот',
      errors: bot.errors,
    })),
  ];

  if (broken.length === 0) {
    return (
      <div
        role="status"
        className="absolute top-[76px] left-1/2 z-20 flex w-[640px] -translate-x-1/2 items-start gap-3 rounded-[10px] border border-[#6b5326] bg-warn-bg p-4 shadow-2xl"
      >
        <span className="mt-1 size-2.5 shrink-0 rounded-full bg-warn" />
        <div className="flex-1 space-y-1 text-sm">
          <p className="font-semibold">
            {rejection.conflict
              ? 'Библиотеку изменили одновременно с сохранением'
              : 'Сохранить не удалось'}
          </p>
          <p className="text-xs text-muted">
            {rejection.conflict
              ? 'Правки остались в редакторе и в черновике — сохраните ещё раз.'
              : rejection.message}
          </p>
        </div>
        <button type="button" aria-label="Закрыть" onClick={onClose} className="text-muted">
          ×
        </button>
      </div>
    );
  }

  return (
    <Dialog
      title="Правка не сохранена"
      width={600}
      actions={
        <Button look="primary" onClick={onClose}>
          Вернуться к правке
        </Button>
      }
    >
      <p className="text-sm text-muted">
        Правка сломала бы места ниже. Исправьте их или верните правку.
      </p>
      <ul className="space-y-2">
        {broken.map((item) => (
          <li
            key={item.id}
            className="space-y-1 rounded-md border border-line bg-panel-2 p-3 text-sm"
          >
            <p>
              <span className="font-semibold">{item.title}</span>{' '}
              <span className="text-xs text-faint">{item.kind}</span>
            </p>
            {Object.entries(item.errors).map(([key, message]) => (
              <p key={key} className="text-xs text-muted">
                <span className="font-mono text-text">{key}</span> {message}
              </p>
            ))}
          </li>
        ))}
      </ul>
      <p className="text-xs text-faint">Ничего не сохранено, правка осталась в черновике.</p>
    </Dialog>
  );
}
