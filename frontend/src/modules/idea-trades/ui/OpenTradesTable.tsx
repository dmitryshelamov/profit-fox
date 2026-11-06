import { useEffect, useState } from 'react';

import { sideLabel, typeLabel } from '@/modules/ideas';
import { ApiError } from '@/shared/api/client';
import { formatMoment } from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import { useCancelTrade, useCloseTrade } from '../api';
import { durationText, promiseHint, promiseText } from '../model/journal';
import { resultText } from '../model/result';
import { canCancel, canClose } from '../model/status';
import {
  byParts,
  cell,
  head,
  Heads,
  type IdeaTrade,
  InstrumentCells,
  LevelCells,
  StatusCell,
} from './tradeCells';

type Asked = { trade: IdeaTrade; action: 'cancel' | 'close' };

const wording = {
  cancel: {
    title: 'Отменить сделку?',
    text: 'Вход будет снят с биржи. Если он успеет исполниться, сделка станет открытой.',
    confirm: 'Отменить сделку',
    keep: 'Не отменять',
  },
  close: {
    title: 'Закрыть сделку по рынку?',
    text: 'Стоп и цель будут сняты с биржи, а позиция сделки закроется рыночным ордером.',
    confirm: 'Закрыть по рынку',
    keep: 'Не закрывать',
  },
} as const;

/** The question before a cancel or a close: nothing is sent until the owner confirms. */
function Confirm({ asked, onDone }: { asked: Asked; onDone: () => void }) {
  const cancel = useCancelTrade();
  const close = useCloseTrade();
  const mutation = asked.action === 'cancel' ? cancel : close;
  const words = wording[asked.action];

  return (
    <Dialog
      title={words.title}
      onClose={onDone}
      actions={
        <>
          <Button look="ghost" onClick={onDone}>
            {words.keep}
          </Button>
          <Button
            look={asked.action === 'close' ? 'primary' : 'danger'}
            disabled={mutation.isPending}
            onClick={() => mutation.mutate(asked.trade.id, { onSuccess: onDone })}
          >
            {words.confirm}
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted">
        {asked.trade.symbol}, {typeLabel(asked.trade.type)}, {sideLabel(asked.trade.side)}.{' '}
        {words.text}
      </p>
      {mutation.error && (
        <p role="alert" className="text-sm text-err">
          {mutation.error instanceof ApiError
            ? mutation.error.message
            : 'Не удалось отправить запрос.'}
        </p>
      )}
    </Dialog>
  );
}

/** The moment now in milliseconds, taken again every half a minute so the time in a trade runs on while the data stands. */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30_000);

    return () => clearInterval(timer);
  }, []);

  return now;
}

/** The trades that wait for their entry or are open; a trade that waits can be cancelled and an open one closed, each after a question. */
export function OpenTradesTable({ trades }: { trades: readonly IdeaTrade[] }) {
  const [asked, setAsked] = useState<Asked | null>(null);
  const now = useNow();

  return (
    <>
      <div className="overflow-x-auto">
        <table
          aria-label="Сделки в работе"
          className="w-full border-separate border-spacing-y-1 text-sm"
        >
          <thead className={head}>
            <tr>
              <Heads
                names={[
                  'Открыта',
                  'Инструмент',
                  'Сетап',
                  'Сторона',
                  'Вход',
                  'Стоп',
                  'Цель',
                  'Объём',
                  'Риск',
                  'Статус',
                  'В сделке',
                  'Сейчас',
                  'Обещание',
                  '',
                ]}
                right={['Вход', 'Стоп', 'Цель', 'Объём', 'Риск']}
              />
            </tr>
          </thead>
          <tbody>
            {trades.map((trade) => (
              <tr key={trade.id} className="bg-panel">
                <td className={`${cell} font-mono whitespace-nowrap text-muted`}>
                  {formatMoment(trade.openedAt ?? trade.createdAt)}
                </td>
                <InstrumentCells trade={trade} />
                <LevelCells trade={trade} risk />
                <StatusCell trade={trade} />
                <td className={`${cell} whitespace-nowrap`}>
                  {trade.openedAt === null
                    ? '—'
                    : durationText((now - Date.parse(trade.openedAt)) / 1000)}
                </td>
                <td className={`${cell} font-mono`}>
                  {byParts(resultText(trade.currentResult, trade.currentResultR))}
                </td>
                <td
                  className={`${cell} font-mono`}
                  title={trade.promise === null ? undefined : promiseHint(trade.promise)}
                >
                  {byParts(promiseText(trade.promise))}
                </td>
                <td className={`${cell} text-right whitespace-nowrap`}>
                  {canCancel(trade.status) && (
                    <Button look="ghost" onClick={() => setAsked({ trade, action: 'cancel' })}>
                      Отменить
                    </Button>
                  )}
                  {canClose(trade.status) && (
                    <Button look="ghost" onClick={() => setAsked({ trade, action: 'close' })}>
                      Закрыть по рынку
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {asked && <Confirm asked={asked} onDone={() => setAsked(null)} />}
    </>
  );
}
