/** A check that got no answer: saving stays possible, since the server checks every save. */
export function CheckFailedNotice({ onRetry }: { onRetry: () => void }) {
  return (
    <div
      role="status"
      className="absolute top-[76px] left-1/2 z-20 flex w-[560px] -translate-x-1/2 items-center gap-3 rounded-[10px] border border-[#6b5326] bg-warn-bg px-4 py-3 text-sm shadow-2xl"
    >
      <span className="size-2.5 shrink-0 rounded-full bg-warn" />
      <span className="flex-1">
        Проверка не удалась: сервер не ответил. Сохранить можно — сервер проверит граф при
        сохранении.
      </span>
      <button type="button" onClick={onRetry} className="font-semibold text-accent">
        Повторить проверку
      </button>
    </div>
  );
}
