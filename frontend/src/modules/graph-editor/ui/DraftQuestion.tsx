import { buttonClass } from './Card';

/** The question about an unsaved draft found on opening a signal (spec signal-constructor «Черновик в браузере»). */
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
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/60">
      <section
        role="dialog"
        aria-label="Черновик"
        className="flex w-96 flex-col gap-3 rounded-lg border border-zinc-700 bg-zinc-900 p-4 text-sm"
      >
        <h2 className="font-semibold">Есть несохранённый черновик</h2>
        <p className="text-zinc-300">
          Черновик от {savedAt.slice(0, 16).replace('T', ' ')} UTC отличается от файла: узлов{' '}
          {nodes}, проводов {wires}
          {nodes === 0 && wires === 0 ? ', расположение или описание' : ''}.
        </p>
        <span className="flex justify-end gap-2">
          <button type="button" className={buttonClass} onClick={onDiscard}>
            Отбросить
          </button>
          <button type="button" className={`${buttonClass} border-amber-500`} onClick={onRestore}>
            Восстановить
          </button>
        </span>
      </section>
    </div>
  );
}
