import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { actOnBot, type BotAction, LiveStatusBadge } from '@/modules/bots';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

/** What a confirmation of an action with money tells the owner. */
interface Confirmation {
  readonly action: BotAction;
  readonly title: string;
  readonly text: string;
}

/**
 * The status of the bot in live trading with the actions its status allows. A start and a close of the position spend money,
 * so they ask first; a refusal of the server shows above the actions and leaves the status as it was.
 */
export function LiveControls({
  botId,
  status,
  reason,
  closing,
  start,
  close,
}: {
  botId: string;
  status: string;
  reason: string | null | undefined;
  /** A Worker closes the position: nothing is offered until it is closed. */
  closing: boolean;
  /** What the bot will trade: its account, instrument and balance. */
  start: string;
  /** The market order that closes the open position; none without one. */
  close: string | undefined;
}) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState<Confirmation>();
  const [failure, setFailure] = useState<string>();
  const [busy, setBusy] = useState(false);
  const stopped = (status === 'stopped' || status === 'failed') && !closing;

  async function act(action: BotAction) {
    setConfirming(undefined);
    setFailure(undefined);
    setBusy(true);

    try {
      await actOnBot(botId, action);
      await queryClient.invalidateQueries({ queryKey: ['bots'] });
    } catch (error) {
      setFailure((error as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section
      aria-label="Торговля"
      className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <LiveStatusBadge status={status} />
          {status === 'failed' && reason && <span className="text-sm text-err">{reason}</span>}
          {closing && <span className="text-sm text-muted">Позиция закрывается по рынку…</span>}
        </div>
        <div className="flex gap-2">
          {stopped && (
            <Button
              look="primary"
              disabled={busy}
              onClick={() =>
                setConfirming({ action: 'start', title: 'Запустить бота?', text: start })
              }
            >
              Запустить
            </Button>
          )}
          {stopped && close && (
            <Button
              disabled={busy}
              onClick={() =>
                setConfirming({ action: 'close-position', title: 'Закрыть позицию?', text: close })
              }
            >
              Закрыть позицию
            </Button>
          )}
          {status === 'running' && (
            <Button disabled={busy} onClick={() => void act('pause')}>
              Пауза
            </Button>
          )}
          {status === 'paused' && (
            <Button disabled={busy} onClick={() => void act('resume')}>
              Продолжить
            </Button>
          )}
          {(status === 'running' || status === 'paused') && (
            <Button look="danger" disabled={busy} onClick={() => void act('stop')}>
              Остановить
            </Button>
          )}
        </div>
      </div>
      {failure && (
        <p
          role="alert"
          className="rounded-md border border-[#5a2a2e] bg-err-bg p-3 text-sm text-err"
        >
          {failure}
        </p>
      )}
      {confirming && (
        <Dialog
          title={confirming.title}
          actions={
            <>
              <Button onClick={() => setConfirming(undefined)}>Отмена</Button>
              <Button look="primary" onClick={() => void act(confirming.action)}>
                {confirming.action === 'start' ? 'Запустить' : 'Закрыть позицию'}
              </Button>
            </>
          }
        >
          <p className="text-sm text-muted">{confirming.text}</p>
        </Dialog>
      )}
    </section>
  );
}
