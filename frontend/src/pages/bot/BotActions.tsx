import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { deleteBot } from '@/modules/bots';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

type Open = 'menu' | 'delete' | undefined;

export function BotActions({ botId, name }: { botId: string; name: string }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [open, setOpen] = useState<Open>();
  const [failure, setFailure] = useState<string>();

  async function remove() {
    setOpen(undefined);

    try {
      await deleteBot(botId);
    } catch (error) {
      setFailure((error as Error).message);

      return;
    }

    queryClient.removeQueries({ queryKey: ['bots', botId] });
    await queryClient.invalidateQueries({ queryKey: ['bots'] });
    await navigate({ to: '/bots' });
  }

  return (
    <div className="relative">
      <Button
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
            className="w-full rounded-md px-3 py-2 text-left text-err hover:bg-panel-2"
            onClick={() => setOpen('delete')}
          >
            Удалить бота
          </button>
        </div>
      )}
      {open === 'delete' && (
        <Dialog
          title={`Удалить бота «${name}»?`}
          actions={
            <>
              <Button onClick={() => setOpen(undefined)}>Отмена</Button>
              <Button look="danger" onClick={() => void remove()}>
                Удалить
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">Вместе с ботом удаляются все его бэктесты.</p>
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
