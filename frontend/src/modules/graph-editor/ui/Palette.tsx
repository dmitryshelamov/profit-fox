import { useState } from 'react';

import { TextField } from '@/shared/ui/TextField';

import type { SignalEntry } from '../model/signals';
import type { NodeKind } from '../model/types';
import { paletteMime, signalMime } from './GraphCanvas';
import { groupFill, groupOf } from './looks';

/**
 * The kinds a document may add, by group, next to the column of tools (spec signal-constructor «Поиск в палитре
 * узлов»): a search field in focus keeps the kinds whose title or kind holds the text, whatever the case, and hides a
 * group without them. A click adds a node in the middle of the view, a drag puts it where it falls. A strategy also
 * offers its saved signals, in the group «Сигналы» (spec strategy-constructor «Каталог узлов стратегии»), found by
 * their names.
 */
export function Palette({
  kinds,
  signals,
  onAdd,
  onAddSignal,
}: {
  readonly kinds: readonly NodeKind[];
  readonly signals?: readonly SignalEntry[];
  readonly onAdd: (kind: NodeKind) => void;
  readonly onAddSignal?: (signal: SignalEntry) => void;
}) {
  const [search, setSearch] = useState('');
  const wanted = search.trim().toLowerCase();
  const foundKinds = kinds.filter(
    (kind) => kind.title.toLowerCase().includes(wanted) || kind.kind.toLowerCase().includes(wanted),
  );
  const foundSignals = signals?.filter((signal) => signal.name.toLowerCase().includes(wanted));
  const groups = [...new Set(foundKinds.map((kind) => kind.group))];
  // Without a search the group of signals is always there, to say that none is saved.
  const showSignals = foundSignals !== undefined && (wanted === '' || foundSignals.length > 0);
  const nothing = groups.length === 0 && !showSignals;

  return (
    <section
      role="region"
      aria-label="Палитра"
      className="pointer-events-auto flex max-h-full w-60 flex-col rounded-[10px] border border-line bg-panel shadow-2xl"
    >
      <div className="p-2.5">
        <TextField
          aria-label="Найти узел"
          type="search"
          sign="⌕"
          placeholder="Найти узел"
          autoFocus
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-1.5 pb-1.5">
        {groups.map((group) => (
          <div key={group} role="group" aria-label={group} className="mb-1.5">
            <h3 className="px-2 py-1.5 text-[11px] font-semibold tracking-wide text-muted uppercase">
              {group}
            </h3>
            {foundKinds
              .filter((kind) => kind.group === group)
              .map((kind) => (
                <button
                  key={kind.kind}
                  type="button"
                  draggable
                  className={itemClass}
                  onDragStart={(event) => event.dataTransfer.setData(paletteMime, kind.kind)}
                  onClick={() => onAdd(kind)}
                >
                  <span
                    aria-hidden="true"
                    className={`size-2.5 shrink-0 rounded-[3px] ${groupFill(groupOf(kind.kind, kind.group))}`}
                  />
                  <span className="shrink-0">{kind.title}</span>
                  <span
                    aria-hidden="true"
                    className="ml-auto min-w-0 truncate pl-2 font-mono text-[10px] text-faint"
                  >
                    {pinsHint(kind)}
                  </span>
                </button>
              ))}
          </div>
        ))}
        {showSignals && (
          <div className="mb-1.5" role="group" aria-label="Сигналы">
            <h3 className="px-2 py-1.5 text-[11px] font-semibold tracking-wide text-muted uppercase">
              Сигналы
            </h3>
            {foundSignals.length === 0 && (
              <p className="px-2 py-1 text-xs text-faint">Сохранённых сигналов нет.</p>
            )}
            {foundSignals.map((signal) => (
              <button
                key={signal.id}
                type="button"
                draggable
                className={`${itemClass} font-mono before:size-2.5 before:shrink-0 before:rounded-[3px] before:bg-group-signal before:content-['']`}
                onDragStart={(event) =>
                  event.dataTransfer.setData(signalMime, JSON.stringify(signal))
                }
                onClick={() => onAddSignal?.(signal)}
              >
                {signal.name}
              </button>
            ))}
          </div>
        )}
        {nothing && (
          <p role="status" className="px-2 py-3 text-xs text-muted">
            Узлов не найдено
          </p>
        )}
      </div>
      <p className="border-t border-line px-3.5 py-2.5 text-[11px] text-faint">
        Клик добавляет узел в центр холста
      </p>
    </section>
  );
}

const itemClass =
  'flex w-full cursor-pointer items-center gap-2.5 rounded-md px-2 py-1.5 text-left text-[13px] hover:bg-panel-2';

/** The pins of a kind in a few signs, as «a, b → result»; a kind whose pins come from the graph has none to name. */
function pinsHint(kind: NodeKind): string {
  const inputs = (kind.inputs ?? []).map((pin) => pin.name).join(', ');
  const outputs = (kind.outputs ?? []).map((pin) => pin.name).join(', ');
  return inputs !== '' && outputs !== '' ? `${inputs} → ${outputs}` : inputs || outputs;
}
