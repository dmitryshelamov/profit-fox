import { useBlocker } from '@tanstack/react-router';
import { useEffect } from 'react';

import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import type { DraftOffer } from '../editing/useGraphEditing';

/** The offer to restore a draft left in this browser. */
export function DraftOfferDialog({
  offer,
  what,
  onRestore,
  onDrop,
}: {
  offer: DraftOffer;
  what: string;
  onRestore: () => void;
  onDrop: () => void;
}) {
  const { nodesAdded, nodesRemoved, wiresAdded, wiresRemoved } = offer.difference;
  const savedAt = new Date(offer.savedAt).toLocaleString('ru-RU', {
    dateStyle: 'long',
    timeStyle: 'short',
  });

  return (
    <Dialog
      title="Найден несохранённый черновик"
      actions={
        <>
          <Button onClick={onDrop}>Отбросить черновик</Button>
          <Button look="primary" onClick={onRestore}>
            Восстановить
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted">
        В этом браузере остался черновик {what} от {savedAt}. Он отличается от сохранённой версии:
        узлы: +{nodesAdded} −{nodesRemoved}, провода: +{wiresAdded} −{wiresRemoved}.
      </p>
      <p className="rounded-md bg-warn-bg px-3 py-2 text-xs text-warn">
        Черновик может быть невалидным — его проверят перед сохранением.
      </p>
    </Dialog>
  );
}

/** Asks before leaving the editor with unsaved edits: inside the app with a dialog, closing the tab with the browser's. */
export function LeaveGuard({ dirty }: { dirty: boolean }) {
  const blocker = useBlocker({
    shouldBlockFn: () => dirty,
    enableBeforeUnload: false,
    withResolver: true,
  });

  // The router asks only with a browser history; the editor asks itself, the same in every history.
  useEffect(() => {
    if (!dirty) {
      return;
    }

    const ask = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', ask);

    return () => window.removeEventListener('beforeunload', ask);
  }, [dirty]);

  if (blocker.status !== 'blocked') {
    return null;
  }

  return (
    <Dialog
      title="Уйти без сохранения?"
      actions={
        <>
          <Button onClick={blocker.reset}>Остаться</Button>
          <Button look="danger" onClick={blocker.proceed}>
            Уйти
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted">Правки останутся черновиком в этом браузере.</p>
    </Dialog>
  );
}
