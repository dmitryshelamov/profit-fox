import { Panel, useReactFlow } from '@xyflow/react';
import { type ReactNode, useState } from 'react';

import type { GraphFlowEdge, GraphFlowNode } from '../model/flow';
import type { CatalogNode } from '../model/types';
import { Palette, type PaletteItem } from './Palette';

const pinTypes = [
  ['число', 'var(--color-pin-number)'],
  ['логическое', 'var(--color-pin-boolean)'],
  ['длительность', 'var(--color-pin-duration)'],
] as const;

const bar = 'rounded-[10px] border border-line bg-panel shadow-xl';

function Tool({
  label,
  active,
  onClick,
  children,
}: {
  label: string;
  active?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-expanded={active}
      onClick={onClick}
      className={`grid size-[34px] place-items-center rounded-[7px] text-[15px] font-semibold ${active ? 'bg-accent text-on-accent' : 'hover:bg-panel-2'}`}
    >
      {children}
    </button>
  );
}

/** The tools column, the zoom bar and the legend of the mockups, floating over the canvas. */
export function CanvasChrome({
  catalog,
  composite,
  readOnly,
  onAdd,
  onFind,
}: {
  catalog: readonly CatalogNode[];
  composite: boolean;
  readOnly: boolean;
  onAdd: (item: PaletteItem, center: { x: number; y: number }) => void;
  /** Selects the node whose title or id contains the text and returns its id. */
  onFind: (text: string) => string | undefined;
}) {
  const flow = useReactFlow<GraphFlowNode, GraphFlowEdge>();
  const [open, setOpen] = useState<'palette' | 'find'>();
  const [find, setFind] = useState('');

  function center() {
    const box = document.querySelector('.react-flow')?.getBoundingClientRect();

    return flow.screenToFlowPosition({
      x: (box?.x ?? 0) + (box?.width ?? 0) / 2,
      y: (box?.y ?? 0) + (box?.height ?? 0) / 2,
    });
  }

  function focus(text: string) {
    const nodeId = onFind(text);

    if (nodeId) {
      void flow.fitView({ nodes: [{ id: nodeId }], maxZoom: 1.2, duration: 300 });
      setOpen(undefined);
      setFind('');
    }
  }

  return (
    <>
      <Panel position="top-left" className="!mt-[60px] flex items-start gap-2">
        {!readOnly && (
          <div className={`${bar} flex flex-col gap-0.5 p-[5px]`}>
            <Tool
              label="Добавить узел"
              active={open === 'palette'}
              onClick={() => setOpen(open === 'palette' ? undefined : 'palette')}
            >
              +
            </Tool>
            <Tool
              label="Найти узел на холсте"
              active={open === 'find'}
              onClick={() => setOpen(open === 'find' ? undefined : 'find')}
            >
              ⌕
            </Tool>
            <Tool label="Вписать в экран" onClick={() => void flow.fitView({ duration: 300 })}>
              ⤢
            </Tool>
          </div>
        )}
        {open === 'palette' && (
          <Palette
            catalog={catalog}
            composite={composite}
            onAdd={(item) => {
              onAdd(item, center());
              setOpen(undefined);
            }}
          />
        )}
        {open === 'find' && (
          <input
            type="search"
            aria-label="Узел на холсте"
            placeholder="Название или id узла, Enter"
            autoFocus
            value={find}
            onChange={(event) => setFind(event.target.value)}
            onKeyDown={(event) => event.key === 'Enter' && focus(find)}
            className={`${bar} h-11 w-64 px-3 text-sm`}
          />
        )}
      </Panel>
      <Panel
        position="bottom-left"
        className={`${bar} flex items-center gap-5 px-3.5 py-2 text-[11.5px] text-muted`}
      >
        {pinTypes.map(([title, color]) => (
          <span key={title} className="flex items-center gap-1.5">
            <span className="size-[9px] rounded-full" style={{ background: color }} />
            {title}
          </span>
        ))}
      </Panel>
      <Panel position="bottom-right" className={`${bar} flex items-center gap-1 p-[5px] text-sm`}>
        <Tool label="Уменьшить" onClick={() => void flow.zoomOut()}>
          −
        </Tool>
        <Tool label="Увеличить" onClick={() => void flow.zoomIn()}>
          +
        </Tool>
        <span className="mx-1 h-5 w-px bg-line" />
        <Tool label="Вписать граф в экран" onClick={() => void flow.fitView({ duration: 300 })}>
          ⤢
        </Tool>
      </Panel>
    </>
  );
}
