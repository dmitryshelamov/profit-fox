import type { ReactFlowInstance } from '@xyflow/react';
import { type ReactNode, useCallback, useMemo, useRef, useState } from 'react';

import type { RequestError } from '@/api/client';

import type { DocumentRules } from '../document';
import type { GraphFlowEdge, GraphFlowNode } from '../model/flow';
import { issuesOf } from '../model/issues';
import { addNode, relayout } from '../model/operations';
import { addSignalNode, type SignalEntry } from '../model/signals';
import type { Graph, GraphDocument, NodeKind } from '../model/types';
import { buttonClass } from './Card';
import { ErrorsCard } from './ErrorsCard';
import { GraphCanvas } from './GraphCanvas';
import { JsonCard } from './JsonCard';
import { NodeCard } from './NodeCard';
import { Palette } from './Palette';
import { ParametersCard } from './ParametersCard';

type Sheet = 'errors' | 'parameters' | 'json';

/**
 * What the editor of a strategy adds for its signal nodes: the saved signals of the palette, and the update of a node
 * to the latest version of its signal, which the page makes because it reads the new version.
 */
export interface SignalTools {
  readonly palette: readonly SignalEntry[];
  readonly onUpdate: (node: string) => void;
}

/**
 * The editor of a graph on a full-screen canvas with floating bars, as in profit-fox-v2: top left the title, top right
 * the errors, parameters, JSON, «Разложить» and the actions of the page, left the palette, on the right one card at a
 * time, and the bottom panel under the canvas.
 */
export function GraphEditor({
  title,
  document,
  catalog,
  rules,
  errors,
  checkFailed,
  readOnly,
  actions,
  bottom,
  signals,
  onChange,
}: {
  readonly title: ReactNode;
  readonly document: GraphDocument;
  readonly catalog: readonly NodeKind[];
  readonly rules: DocumentRules;
  readonly errors: readonly RequestError[];
  readonly checkFailed: boolean;
  readonly readOnly: boolean;
  readonly actions?: ReactNode;
  readonly bottom?: ReactNode;
  readonly signals?: SignalTools;
  /** <c>urgent</c>: the change added or updated a signal node, whose pins come with the next check. */
  readonly onChange: (document: GraphDocument, urgent?: boolean) => void;
}) {
  const [selected, setSelected] = useState<string>();
  const [sheet, setSheet] = useState<Sheet>();
  const issues = useMemo(() => issuesOf(errors), [errors]);
  const flow = useRef<ReactFlowInstance<GraphFlowNode, GraphFlowEdge>>(null);

  const select = useCallback((node: string | undefined) => {
    setSelected(node);
    if (node !== undefined) {
      setSheet(undefined);
    }
  }, []);
  const toggle = (next: Sheet) => {
    setSelected(undefined);
    setSheet((current) => (current === next ? undefined : next));
  };

  const center = () =>
    flow.current?.screenToFlowPosition({ x: window.innerWidth / 2, y: window.innerHeight / 3 }) ?? {
      x: 0,
      y: 0,
    };

  const add = (kind: NodeKind) => {
    const added = addNode(document, catalog, kind, center());
    onChange(added.document);
    select(added.id);
  };

  const addSignal = (signal: SignalEntry) => {
    const added = addSignalNode(document, catalog, signal, center());
    onChange(added.document, true);
    select(added.id);
  };

  return (
    <div className="flex h-screen flex-col bg-zinc-950 text-zinc-100">
      <div className="relative min-h-0 flex-1">
        <GraphCanvas
          document={document}
          catalog={catalog}
          rules={rules}
          issues={issues}
          readOnly={readOnly}
          selected={selected}
          onChange={onChange}
          onSelect={select}
          onInit={(instance) => {
            flow.current = instance;
          }}
        />
        <div className="pointer-events-none absolute inset-0 flex flex-col gap-3 p-3">
          <div className="flex items-start justify-between gap-3">
            <div className="pointer-events-auto flex items-center gap-3 rounded-lg border border-zinc-800 bg-zinc-900/90 px-3 py-2 text-sm">
              {title}
            </div>
            <div className="pointer-events-auto flex flex-wrap items-center justify-end gap-2 rounded-lg border border-zinc-800 bg-zinc-900/90 px-2 py-1.5">
              <button
                type="button"
                className={`${buttonClass} ${errors.length > 0 ? 'border-red-700 text-red-300' : ''}`}
                aria-pressed={sheet === 'errors'}
                onClick={() => toggle('errors')}
              >
                Ошибки {errors.length}
              </button>
              <button
                type="button"
                className={buttonClass}
                aria-pressed={sheet === 'parameters'}
                onClick={() => toggle('parameters')}
              >
                Параметры
              </button>
              <button
                type="button"
                className={buttonClass}
                aria-pressed={sheet === 'json'}
                onClick={() => toggle('json')}
              >
                JSON
              </button>
              {!readOnly && (
                <button
                  type="button"
                  className={buttonClass}
                  onClick={() => onChange(relayout(document))}
                >
                  Разложить
                </button>
              )}
              {actions}
            </div>
          </div>
          <div className="flex min-h-0 flex-1 items-start justify-between gap-3">
            {readOnly ? (
              <span />
            ) : (
              <Palette
                kinds={rules.palette(catalog)}
                signals={signals?.palette}
                onAdd={add}
                onAddSignal={addSignal}
              />
            )}
            {selected !== undefined && (
              <NodeCard
                key={selected}
                document={document}
                catalog={catalog}
                rules={rules}
                issues={issues}
                readOnly={readOnly}
                node={selected}
                onChange={onChange}
                onSelect={select}
                onUpdateSignal={signals?.onUpdate}
              />
            )}
            {sheet === 'errors' && (
              <ErrorsCard
                issues={issues}
                failed={checkFailed}
                onSelect={select}
                onClose={() => setSheet(undefined)}
              />
            )}
            {sheet === 'parameters' && (
              <ParametersCard
                document={document}
                readOnly={readOnly}
                onChange={onChange}
                onClose={() => setSheet(undefined)}
              />
            )}
            {sheet === 'json' && (
              <JsonCard
                graph={document.graph}
                readOnly={readOnly}
                onReplace={(graph: Graph) => onChange({ graph, layout: {} })}
                onClose={() => setSheet(undefined)}
              />
            )}
          </div>
        </div>
      </div>
      {bottom}
    </div>
  );
}
