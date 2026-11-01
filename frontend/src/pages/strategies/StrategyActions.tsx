import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { deleteStrategy, renameStrategy, strategiesQuery } from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

type Open = 'menu' | 'rename' | 'delete' | undefined;

export function StrategyActions({ id, name }: { id: string; name: string }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState<Open>();
  const [newName, setNewName] = useState(name);
  const [failure, setFailure] = useState<string>();

  async function run(action: () => Promise<void>) {
    setOpen(undefined);
    setFailure(undefined);

    try {
      await action();
    } catch (error) {
      setFailure(describe(error));
    }

    await queryClient.invalidateQueries({ queryKey: strategiesQuery.queryKey });
  }

  return (
    <div className="relative flex justify-end">
      <Button
        look="plain"
        aria-label={`Действия: ${name}`}
        aria-expanded={open === 'menu'}
        onClick={() => setOpen(open === 'menu' ? undefined : 'menu')}
      >
        ⋯
      </Button>
      {open === 'menu' && (
        <div
          role="menu"
          className="absolute top-9 right-0 z-10 w-48 rounded-lg border border-line bg-panel p-1 shadow-xl"
        >
          <button
            role="menuitem"
            className="w-full rounded-md px-3 py-2 text-left hover:bg-panel-2"
            onClick={() => {
              setNewName(name);
              setOpen('rename');
            }}
          >
            Переименовать
          </button>
          <button
            role="menuitem"
            className="w-full rounded-md px-3 py-2 text-left text-err hover:bg-panel-2"
            onClick={() => setOpen('delete')}
          >
            Удалить стратегию
          </button>
        </div>
      )}
      {open === 'rename' && (
        <Dialog
          title={`Переименовать «${name}»`}
          actions={
            <>
              <Button onClick={() => setOpen(undefined)}>Отмена</Button>
              <Button
                look="primary"
                disabled={!newName.trim()}
                onClick={() => void run(() => renameStrategy(id, newName.trim()))}
              >
                Сохранить
              </Button>
            </>
          }
        >
          <label className="block space-y-1.5 text-[11px] font-semibold tracking-wide text-muted uppercase">
            Название
            <input
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              className="block h-8 w-full rounded-md border border-line bg-panel-2 px-2.5 text-sm font-normal tracking-normal text-text normal-case"
            />
          </label>
        </Dialog>
      )}
      {open === 'delete' && (
        <Dialog
          title={`Удалить стратегию «${name}»?`}
          actions={
            <>
              <Button onClick={() => setOpen(undefined)}>Отмена</Button>
              <Button look="danger" onClick={() => void run(() => deleteStrategy(id))}>
                Удалить
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">Вместе со стратегией удаляются все её версии.</p>
        </Dialog>
      )}
      {failure && (
        <p
          role="alert"
          className="absolute top-9 right-0 z-10 w-80 rounded-lg border border-[#5a2a2e] bg-err-bg p-3 text-left text-sm"
        >
          {failure}
        </p>
      )}
    </div>
  );
}

function describe(error: unknown): string {
  if (error instanceof ApiError && error.bots.length > 0) {
    return `Удалить нельзя: на стратегии созданы боты ${error.bots.map((bot) => `«${bot.name}»`).join(', ')}.`;
  }

  return (error as Error).message;
}
