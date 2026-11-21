import { useState } from 'react';

import type { SignalEntry } from '../model/signals';
import type { NodeKind } from '../model/types';
import { paletteMime, signalMime } from './GraphCanvas';

/**
 * The kinds a document may add, by group: a click adds one in the middle of the view, a drag puts it where it falls. A
 * strategy also offers its saved signals, in the group «Сигналы» (spec strategy-constructor «Каталог узлов стратегии»).
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
  const [open, setOpen] = useState(false);
  const groups = [...new Set(kinds.map((kind) => kind.group))];

  return (
    <div className="pointer-events-auto flex flex-col items-start gap-2">
      <button
        type="button"
        aria-expanded={open}
        aria-label="Палитра узлов"
        className="grid size-9 place-items-center rounded-lg border border-zinc-700 bg-zinc-900 text-lg hover:bg-zinc-800"
        onClick={() => setOpen((current) => !current)}
      >
        +
      </button>
      {open && (
        <section
          role="region"
          aria-label="Палитра"
          className="w-56 rounded-lg border border-zinc-700 bg-zinc-900/95 p-2 text-sm shadow-2xl"
        >
          {groups.map((group) => (
            <div key={group} className="mb-2">
              <h3 className="px-1 text-xs text-zinc-500">{group}</h3>
              {kinds
                .filter((kind) => kind.group === group)
                .map((kind) => (
                  <button
                    key={kind.kind}
                    type="button"
                    draggable
                    className="block w-full rounded px-2 py-1 text-left hover:bg-zinc-800"
                    onDragStart={(event) => event.dataTransfer.setData(paletteMime, kind.kind)}
                    onClick={() => onAdd(kind)}
                  >
                    {kind.title}
                  </button>
                ))}
            </div>
          ))}
          {signals !== undefined && (
            <div className="mb-2" role="group" aria-label="Сигналы">
              <h3 className="px-1 text-xs text-zinc-500">Сигналы</h3>
              {signals.length === 0 && (
                <p className="px-2 py-1 text-xs text-zinc-500">Сохранённых сигналов нет.</p>
              )}
              {signals.map((signal) => (
                <button
                  key={signal.id}
                  type="button"
                  draggable
                  className="block w-full rounded px-2 py-1 text-left font-mono hover:bg-zinc-800"
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
        </section>
      )}
    </div>
  );
}
