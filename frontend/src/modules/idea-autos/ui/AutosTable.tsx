import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { Schemas } from '@/shared/api/client';
import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';

import { useDeleteAuto, useSetAutoMode, useSetAutosOrder } from '../api';
import { conditionLines, modeCaption, modes } from '../model/labels';
import { moveBefore } from '../model/order';
import { paperText, realText } from '../model/totals';

type Item = Schemas['IdeaAutoItemResponse'];

const cell = 'px-2 py-3 align-top';
const heads = [
  '#',
  'Название',
  'Условия',
  'Аккаунт',
  'Сумма',
  'Режим',
  'Учебные',
  'С деньгами',
  'Отказов сегодня',
  '',
];

function ModeSwitch({ item }: { item: Item }) {
  const change = useSetAutoMode();
  const { auto } = item;

  return (
    <div
      role="radiogroup"
      aria-label={`Режим автомата «${auto.name}»`}
      className="flex w-fit rounded-lg bg-app p-0.5 text-sm"
    >
      {modes.map((mode) => (
        <button
          key={mode.value}
          type="button"
          role="radio"
          aria-checked={auto.mode === mode.value}
          disabled={change.isPending}
          onClick={() =>
            auto.mode !== mode.value && change.mutate({ id: auto.id, mode: mode.value })
          }
          className={`h-8 rounded-md px-3 whitespace-nowrap ${
            auto.mode !== mode.value
              ? 'text-muted hover:text-text'
              : mode.value === 'live'
                ? 'bg-accent font-bold text-on-accent'
                : mode.value === 'paper'
                  ? 'bg-run/20 font-bold text-run'
                  : 'bg-panel-2 font-bold text-text'
          }`}
        >
          {mode.label}
        </button>
      ))}
    </div>
  );
}

function Totals({ main, sum }: { main: string; sum: string }) {
  const color = sum === 'не было' ? 'text-faint' : sum.includes('-') ? 'text-err' : 'text-ok';

  return (
    <>
      <span className="block font-mono">{main}</span>
      <span className={`block font-mono text-xs ${color}`}>{sum}</span>
    </>
  );
}

function ConfirmDelete({ item, onDone }: { item: Item; onDone: () => void }) {
  const remove = useDeleteAuto();

  return (
    <Dialog
      title={`Удалить автомат «${item.auto.name}»?`}
      onClose={onDone}
      actions={
        <>
          <Button look="ghost" onClick={onDone}>
            Не удалять
          </Button>
          <Button
            look="danger"
            disabled={remove.isPending}
            onClick={() => remove.mutate(item.auto.id, { onSuccess: onDone })}
          >
            Удалить
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted">
        Сделки автомата остаются в журнале с его названием, а открытые идут до своего исхода. Новых
        идей автомат не берёт.
      </p>
      {remove.error && (
        <p role="alert" className="text-sm text-err">
          {remove.error instanceof ApiError ? remove.error.message : 'Не удалось удалить автомат.'}
        </p>
      )}
    </Dialog>
  );
}

/**
 * The autos in the order of the list, the first the most important: the mode of each is switched at once, the rows are dragged by
 * their handle (or moved by the arrows on it) and the new order is saved at once; a delete is asked about first.
 */
export function AutosTable({ items }: { items: readonly Item[] }) {
  const reorder = useSetAutosOrder();
  const [dragged, setDragged] = useState<string | null>(null);
  const [asked, setAsked] = useState<Item | null>(null);
  const ids = items.map((item) => item.auto.id);
  const save = (next: string[]) => reorder.mutate(next);

  return (
    <>
      <div className="overflow-x-auto">
        <table aria-label="Автоматы" className="w-full border-separate border-spacing-y-1 text-sm">
          <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
            <tr>
              {heads.map((name, index) => (
                <th
                  key={name || index}
                  scope="col"
                  className={name === 'Сумма' ? `${cell} text-right` : cell}
                >
                  {name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {items.map((item, index) => {
              const { auto } = item;
              const lines = conditionLines(auto);
              const paper = paperText(item);
              const real = realText(item);

              return (
                <tr
                  key={auto.id}
                  data-auto={auto.name}
                  onDragOver={(event) => dragged !== null && event.preventDefault()}
                  onDrop={(event) => {
                    event.preventDefault();

                    if (dragged !== null && dragged !== auto.id) {
                      save(moveBefore(ids, dragged, auto.id));
                    }

                    setDragged(null);
                  }}
                  className={`bg-panel ${auto.mode === 'off' ? 'text-muted' : ''} ${dragged === auto.id ? 'opacity-50' : ''}`}
                >
                  <td className={`${cell} font-mono text-muted`}>
                    {index + 1}
                    <span
                      role="button"
                      tabIndex={0}
                      draggable
                      aria-label={`Перетащить «${auto.name}»`}
                      title="Перетащить, чтобы изменить приоритет"
                      onDragStart={(event) => {
                        event.dataTransfer.setData('text/plain', auto.id);
                        event.dataTransfer.effectAllowed = 'move';
                        setDragged(auto.id);
                      }}
                      onDragEnd={() => setDragged(null)}
                      onKeyDown={(event) => {
                        const neighbour = items[index + (event.key === 'ArrowUp' ? -1 : 1)];

                        if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') && neighbour) {
                          event.preventDefault();
                          save(moveBefore(ids, auto.id, neighbour.auto.id));
                        }
                      }}
                      className="mt-1 block cursor-grab text-faint select-none"
                    >
                      ⠿
                    </span>
                  </td>
                  <td className={cell}>
                    <span className="block font-bold text-text">{auto.name}</span>
                    <span
                      className={`block text-xs ${auto.mode === 'live' ? 'text-accent' : auto.mode === 'paper' ? 'text-run' : 'text-faint'}`}
                    >
                      {modeCaption(auto.mode)}
                    </span>
                  </td>
                  <td className={cell}>
                    <span className="block text-text">{lines.what}</span>
                    <span className="block text-xs">{lines.where}</span>
                  </td>
                  <td className={`${cell} whitespace-nowrap text-text`}>{auto.accountName}</td>
                  <td className={`${cell} text-right font-mono whitespace-nowrap text-text`}>
                    {auto.amount} USDT
                  </td>
                  <td className={cell}>
                    <ModeSwitch item={item} />
                  </td>
                  <td className={cell}>
                    <Totals {...paper} />
                  </td>
                  <td className={cell}>
                    <Totals {...real} />
                  </td>
                  <td className={`${cell} font-mono`}>
                    {Number(item.refusalsToday) > 0 ? (
                      <Link
                        to="/idea-trades"
                        search={{ autoId: auto.id }}
                        aria-label={`Отказов сегодня у «${auto.name}»: ${item.refusalsToday}`}
                        className="text-run hover:underline"
                      >
                        {item.refusalsToday}
                      </Link>
                    ) : (
                      <span className="text-faint">0</span>
                    )}
                  </td>
                  <td className={`${cell} text-right whitespace-nowrap`}>
                    <Link
                      to="/idea-trades/autos/$id"
                      params={{ id: auto.id }}
                      aria-label={`Изменить «${auto.name}»`}
                      className="mr-2 inline-flex h-8 items-center rounded-md border border-line px-3 text-sm font-semibold text-text hover:bg-panel-2"
                    >
                      Изменить
                    </Link>
                    <Button
                      look="danger"
                      aria-label={`Удалить «${auto.name}»`}
                      onClick={() => setAsked(item)}
                    >
                      Удалить
                    </Button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {asked && <ConfirmDelete item={asked} onDone={() => setAsked(null)} />}
    </>
  );
}
