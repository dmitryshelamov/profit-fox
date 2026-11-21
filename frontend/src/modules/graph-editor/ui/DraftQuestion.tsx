import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

/**
 * The question about an unsaved draft found on opening a signal (spec signal-constructor «Черновик в браузере»). It
 * waits for one of its two answers: Escape does not close it.
 */
export function DraftQuestion({
  savedAt,
  nodes,
  wires,
  onRestore,
  onDiscard,
}: {
  readonly savedAt: string;
  readonly nodes: number;
  readonly wires: number;
  readonly onRestore: () => void;
  readonly onDiscard: () => void;
}) {
  return (
    <Dialog label="Черновик" title="Есть несохранённый черновик">
      <p className="text-muted">
        Черновик от {savedAt.slice(0, 16).replace('T', ' ')} UTC отличается от файла: узлов {nodes},
        проводов {wires}
        {nodes === 0 && wires === 0 ? ', расположение или описание' : ''}.
      </p>
      <span className="flex justify-end gap-2">
        <Button onClick={onDiscard}>Отбросить</Button>
        <Button variant="primary" onClick={onRestore}>
          Восстановить
        </Button>
      </span>
    </Dialog>
  );
}
