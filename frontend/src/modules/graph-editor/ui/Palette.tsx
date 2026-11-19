import { useState } from 'react';

import type { NodeKind } from '../model/types';
import { paletteMime } from './GraphCanvas';

/** The kinds a document may add, by group: a click adds one in the middle of the view, a drag puts it where it falls. */
export function Palette({
  kinds,
  onAdd,
}: {
  readonly kinds: readonly NodeKind[];
  readonly onAdd: (kind: NodeKind) => void;
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
        </section>
      )}
    </div>
  );
}
