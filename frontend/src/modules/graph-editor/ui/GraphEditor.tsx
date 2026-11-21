import type { ReactFlowInstance } from '@xyflow/react';
import { type ReactNode, useCallback, useMemo, useRef, useState } from 'react';

import type { RequestError } from '@/api/client';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { TextField } from '@/shared/ui/TextField';

import type { DocumentRules } from '../document';
import type { GraphFlowEdge, GraphFlowNode } from '../model/flow';
import { issuesOf } from '../model/issues';
import { addNode, relayout } from '../model/operations';
import { kindOf } from '../model/pins';
import { addSignalNode, type SignalEntry, signalInfo } from '../model/signals';
import type { Graph, GraphDocument, GraphNode, NodeKind } from '../model/types';
import { ErrorsCard } from './ErrorsCard';
import { BarDivider, FloatingBar } from './FloatingBar';
import { fitOptions, GraphCanvas } from './GraphCanvas';
import { JsonCard } from './JsonCard';
import { NodeCard } from './NodeCard';
import { Palette } from './Palette';
import { ParametersCard } from './ParametersCard';

type Sheet = 'errors' | 'parameters' | 'json';
type Tool = 'palette' | 'find';

/**
 * What the editor of a strategy adds for its signal nodes: the saved signals of the palette, and the update of a node
 * to the latest version of its signal, which the page makes because it reads the new version.
 */
export interface SignalTools {
  readonly palette: readonly SignalEntry[];
  readonly onUpdate: (node: string) => void;
}

/**
 * The editor of a graph on a full-screen canvas with floating bars, as in profit-fox-v2 (change redesign-bench-ui,
 * design.md, decision 8): top left the document and its state, top right the errors, parameters, JSON and the actions
 * of the page, under the left bar the column of tools with the palette and the search of a node, on the right one card
 * at a time, at the bottom of the canvas the legend of types and the scale, and the bottom panel under the canvas.
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
  const [tool, setTool] = useState<Tool>();
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
  const toggleTool = (next: Tool) => setTool((current) => (current === next ? undefined : next));

  const center = () =>
    flow.current?.screenToFlowPosition({ x: window.innerWidth / 2, y: window.innerHeight / 3 }) ?? {
      x: 0,
      y: 0,
    };

  // A node added from the palette stands in the middle of the view, and the palette closes.
  const add = (kind: NodeKind) => {
    const added = addNode(document, catalog, kind, center());
    onChange(added.document);
    select(added.id);
    setTool(undefined);
  };

  const addSignal = (signal: SignalEntry) => {
    const added = addSignalNode(document, catalog, signal, center());
    onChange(added.document, true);
    select(added.id);
    setTool(undefined);
  };

  /** The title of a node as the canvas shows it: of its kind, or the name of the signal of a signal node. */
  const titleOf = (node: GraphNode): string =>
    (node.kind === 'signal' ? signalInfo(node, catalog)?.name : undefined) ??
    kindOf(node, catalog)?.title ??
    node.kind;

  /** Selects the first node whose title or id holds the text and brings it into view; false when there is none. */
  const find = (text: string): boolean => {
    const wanted = text.trim().toLowerCase();
    const found = document.graph.nodes.find(
      (node) =>
        node.id.toLowerCase().includes(wanted) || titleOf(node).toLowerCase().includes(wanted),
    );
    if (wanted === '' || found === undefined) {
      return false;
    }

    select(found.id);
    void flow.current?.fitView({ nodes: [{ id: found.id }], maxZoom: 1, padding: 1.5 });
    return true;
  };

  return (
    <div className="flex h-screen flex-col bg-canvas text-text">
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
        {/* The bottom is left to the legend and the scale, which belong to the canvas. */}
        <div className="pointer-events-none absolute inset-0 flex flex-col gap-3 p-4 pb-[76px]">
          <div className="flex items-start justify-between gap-3">
            <FloatingBar className="min-w-0">{title}</FloatingBar>
            <FloatingBar role="toolbar" aria-label="Действия" className="shrink-0 gap-1 px-1.5">
              <Button
                variant="plain"
                className={errors.length > 0 ? 'bg-err-bg/70' : 'aria-pressed:bg-panel-2'}
                aria-pressed={sheet === 'errors'}
                onClick={() => toggle('errors')}
              >
                Ошибки{' '}
                <Badge tone={errors.length > 0 ? 'err' : 'ok'} mono>
                  {errors.length}
                </Badge>
              </Button>
              <Button
                variant="plain"
                className="aria-pressed:bg-panel-2"
                aria-pressed={sheet === 'parameters'}
                onClick={() => toggle('parameters')}
              >
                Параметры
              </Button>
              <Button
                variant="plain"
                className="aria-pressed:bg-panel-2"
                aria-pressed={sheet === 'json'}
                onClick={() => toggle('json')}
              >
                JSON
              </Button>
              {actions !== undefined && (
                <>
                  <BarDivider />
                  {actions}
                </>
              )}
            </FloatingBar>
          </div>
          <div className="flex min-h-0 flex-1 items-start justify-between gap-3">
            <div className="flex max-h-full min-h-0 items-start gap-2">
              <div
                role="toolbar"
                aria-label="Инструменты"
                aria-orientation="vertical"
                className="pointer-events-auto flex w-11 flex-col items-center gap-1 rounded-[10px] border border-line bg-panel/95 py-1.5 shadow-xl"
              >
                {!readOnly && (
                  <ToolButton
                    label="Палитра узлов"
                    hint="Добавить узел"
                    expanded={tool === 'palette'}
                    onClick={() => toggleTool('palette')}
                  >
                    +
                  </ToolButton>
                )}
                <ToolButton
                  label="Найти узел на холсте"
                  expanded={tool === 'find'}
                  onClick={() => toggleTool('find')}
                >
                  ⌕
                </ToolButton>
                <ToolButton
                  label="Вписать в экран"
                  onClick={() => void flow.current?.fitView(fitOptions)}
                >
                  ⤢
                </ToolButton>
                {!readOnly && (
                  <ToolButton label="Выстроить" onClick={() => onChange(relayout(document))}>
                    ⇶
                  </ToolButton>
                )}
              </div>
              {tool === 'palette' && !readOnly && (
                <Palette
                  kinds={rules.palette(catalog)}
                  signals={signals?.palette}
                  onAdd={add}
                  onAddSignal={addSignal}
                />
              )}
              {tool === 'find' && <NodeSearch onFind={find} />}
            </div>
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
                titleOf={(id) => {
                  const node = document.graph.nodes.find((candidate) => candidate.id === id);
                  return node ? titleOf(node) : id;
                }}
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

/** A tool of the column on the left: a square button with a sign; an open one is of the accent color. */
function ToolButton({
  label,
  hint,
  expanded,
  onClick,
  children,
}: {
  readonly label: string;
  readonly hint?: string;
  readonly expanded?: boolean;
  readonly onClick: () => void;
  readonly children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-expanded={expanded}
      title={hint ?? label}
      className="grid size-8 cursor-pointer place-items-center rounded-md text-base leading-none text-muted hover:bg-panel-2 hover:text-text aria-expanded:bg-accent aria-expanded:text-on-accent"
      onClick={onClick}
    >
      {children}
    </button>
  );
}

/**
 * The search of a node on the canvas (spec signal-constructor «Поиск узла на холсте»): Enter selects the first node
 * whose title or id holds the text and shows it; without such a node the field says so and nothing changes.
 */
function NodeSearch({ onFind }: { readonly onFind: (text: string) => boolean }) {
  const [text, setText] = useState('');
  const [missed, setMissed] = useState(false);
  return (
    <form
      role="search"
      aria-label="Поиск узла"
      className="pointer-events-auto flex w-60 flex-col gap-2 rounded-[10px] border border-line bg-panel/95 p-2.5 shadow-2xl"
      onSubmit={(event) => {
        event.preventDefault();
        setMissed(!onFind(text));
      }}
    >
      <TextField
        aria-label="Название или id узла"
        type="search"
        sign="⌕"
        placeholder="Название или id, Enter"
        autoFocus
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setMissed(false);
        }}
      />
      {missed && (
        <p role="status" className="px-1 text-xs text-err">
          Узел не найден
        </p>
      )}
    </form>
  );
}
