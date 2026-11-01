import { useState } from 'react';

import type { CatalogNode } from '../model/types';
import { paletteMime } from './GraphCanvas';
import { compositeColor, nodeGroups } from './looks';

export interface PaletteItem {
  readonly kind: string;
  readonly ref?: string;
}

/** The node library: simple nodes by group and composite nodes; a node is added by a click or dragged onto the canvas. */
export function Palette({
  catalog,
  composite,
  onAdd,
}: {
  catalog: readonly CatalogNode[];
  /** A composite node graph has input and output nodes instead of parameters. */
  composite: boolean;
  onAdd: (item: PaletteItem) => void;
}) {
  const [search, setSearch] = useState('');
  const hidden = composite ? ['parameters'] : ['inputs', 'outputs'];
  const matches = (entry: CatalogNode) =>
    entry.title.toLowerCase().includes(search.trim().toLowerCase());
  const groups = [
    ...nodeGroups.map((group) => ({
      title: group.title,
      color: group.color,
      items: catalog
        .filter((entry) => group.kinds.includes(entry.id) && !hidden.includes(entry.id))
        .map((entry) => ({ entry, item: { kind: entry.id } })),
    })),
    {
      title: 'Составные',
      color: compositeColor,
      items: catalog
        .filter((entry) => entry.group !== 'simple')
        .map((entry) => ({ entry, item: { kind: 'composite', ref: entry.id } })),
    },
  ]
    .map((group) => ({ ...group, items: group.items.filter(({ entry }) => matches(entry)) }))
    .filter((group) => group.items.length > 0);

  return (
    <div
      role="dialog"
      aria-label="Библиотека узлов"
      className="max-h-[calc(100vh-160px)] w-64 overflow-y-auto rounded-xl border border-line bg-panel p-2.5 shadow-2xl"
    >
      <input
        type="search"
        aria-label="Найти узел"
        placeholder="Найти узел…"
        autoFocus
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        className="mb-2 h-8 w-full rounded-md border border-accent bg-panel-2 px-2.5 text-sm"
      />
      {groups.map((group) => (
        <div key={group.title} role="group" aria-label={group.title} className="mb-1">
          <p className="px-1.5 pt-1.5 pb-1 text-[10.5px] font-semibold tracking-wide text-faint uppercase">
            {group.title}
          </p>
          {group.items.map(({ entry, item }) => (
            <button
              key={entry.id}
              type="button"
              aria-label={entry.title}
              draggable
              onDragStart={(event) => {
                event.dataTransfer.setData(paletteMime, JSON.stringify(item));
                event.dataTransfer.effectAllowed = 'copy';
              }}
              onClick={() => onAdd(item)}
              className="flex w-full items-center gap-2.5 rounded-md px-2.5 py-1 text-left text-[13px] hover:bg-panel-2"
            >
              <span
                className="size-[9px] shrink-0 rounded-sm"
                style={{ background: group.color }}
              />
              <span className="flex-1">{entry.title}</span>
              {entry.group !== 'simple' && (
                <span className="text-[11px] text-faint">
                  {entry.group === 'builtIn' ? 'встроенный' : 'мой'}
                </span>
              )}
            </button>
          ))}
        </div>
      ))}
      <p className="px-1.5 pt-2 text-[11.5px] text-faint">Нажмите или перетащите на холст</p>
    </div>
  );
}
