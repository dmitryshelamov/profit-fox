import { useState } from 'react';

import { sideLabel, typeLabel } from '@/modules/ideas';
import { CategoryBadge, setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import { ApiError, type Schemas } from '@/shared/api/client';
import { formatMoment } from '@/shared/format/format';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import { useCancelTrade, useCloseTrade } from '../api';
import { resultText } from '../model/result';
import { canCancel, canClose, statusLabel } from '../model/status';

export type IdeaTrade = Schemas['IdeaTradeResponse'];

const cell = 'px-3 py-3';
const number = `${cell} text-right font-mono whitespace-nowrap`;

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

/** The trades by ideas, the newest first; a trade that waits can be cancelled and an open one closed, each after a question. */
export function TradesTable({ trades }: { trades: readonly IdeaTrade[] }) {
  const [asked, setAsked] = useState<Asked | null>(null);

  return (
    <>
      <table
        aria-label="Сделки по идеям"
        className="w-full border-separate border-spacing-y-1 text-sm"
      >
        <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            {['Время', 'Инструмент', 'Сетап', 'Тип идеи', 'Сторона'].map((label) => (
              <th key={label} scope="col" className={cell}>
                {label}
              </th>
            ))}
            {['Вход', 'Стоп', 'Цель', 'Объём', 'Риск'].map((label) => (
              <th key={label} scope="col" className={`${cell} text-right`}>
                {label}
              </th>
            ))}
            {['Статус', 'Результат', ''].map((label, index) => (
              <th key={label || index} scope="col" className={cell}>
                {label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {trades.map((trade) => (
            <tr key={trade.id} className="bg-panel">
              <td className={`${cell} font-mono whitespace-nowrap text-muted`}>
                {formatMoment(trade.createdAt)}
              </td>
              <td className={`${cell} whitespace-nowrap`}>
                <CategoryBadge category={trade.category} />{' '}
                <span className="ml-1.5 font-semibold">{trade.symbol}</span>
              </td>
              <td className={cell}>
                {setupLabel(trade.setup)} · {intervalLabel(trade.interval)}
              </td>
              <td className={`${cell} whitespace-nowrap`}>{typeLabel(trade.type)}</td>
              <td className={cell}>{sideLabel(trade.side)}</td>
              <td className={number}>{trade.filledPrice ?? trade.entryPrice}</td>
              <td className={number}>{trade.stop}</td>
              <td className={number}>{trade.target}</td>
              <td className={number}>{trade.quantity}</td>
              <td className={number}>{trade.risk}</td>
              <td className={cell}>
                <span>{statusLabel(trade.status)}</span>
                {trade.status === 'failed' && trade.reason !== null && (
                  <span className="mt-1 block max-w-xs text-xs text-err">{trade.reason}</span>
                )}
              </td>
              <td className={`${cell} font-mono whitespace-nowrap`}>
                {resultText(trade.result, trade.resultR)}
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
      {asked && <Confirm asked={asked} onDone={() => setAsked(null)} />}
    </>
  );
}
