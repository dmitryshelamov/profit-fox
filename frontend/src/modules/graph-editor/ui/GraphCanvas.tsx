import '@xyflow/react/dist/style.css';

import {
  applyEdgeChanges,
  applyNodeChanges,
  Background,
  BackgroundVariant,
  type Connection,
  type EdgeChange,
  type NodeChange,
  Panel,
  ReactFlow,
  type ReactFlowInstance,
  useReactFlow,
  useViewport,
} from '@xyflow/react';
import { type DragEvent, useEffect, useMemo, useState } from 'react';

import type { DocumentRules } from '../document';
import { type GraphFlowEdge, type GraphFlowNode, toFlow, wireOf } from '../model/flow';
import type { Issue } from '../model/issues';
import {
  addNode,
  connect,
  disconnect,
  moveNodes,
  removeNode,
  setInputValue,
} from '../model/operations';
import { typeOf } from '../model/pins';
import { addSignalNode, type SignalEntry } from '../model/signals';
import type { Catalog, GraphDocument, NodeKind, PinType } from '../model/types';
import { canConnect } from '../model/wires';
import { type GraphEnvironment, GraphEnvironmentContext } from './GraphContext';
import { GraphNodeView } from './GraphNodeView';
import { pinLooks, pinTypes, wireStrokes } from './looks';

/** The drag-and-drop type of a kind dragged from the palette: its name. */
export const paletteMime = 'application/x-profit-fox-node';

/** The type of the data a saved signal carries when it is dragged from the palette. */
export const signalMime = 'application/x-profit-fox-signal';

const nodeTypes = { graph: GraphNodeView };

/** The whole graph in view, never larger than its real size: a small graph opens at 100 %. */
export const fitOptions = { maxZoom: 1 };

/**
 * The wires as the canvas draws them (spec signal-constructor «Цвет группы узла и типа пина»): each of the color of the
 * type of the output it comes from, a wire with an error of the color of an error. The type is kept in the data of the
 * edge and told by its class `wire-<type>`.
 */
export function coloredWires(
  edges: readonly GraphFlowEdge[],
  document: GraphDocument,
  catalog: Catalog,
  issues: readonly Issue[],
): GraphFlowEdge[] {
  return edges.map((edge): GraphFlowEdge => {
    const { from } = wireOf(edge);
    const type: PinType | undefined = typeOf(document.graph, catalog, from, 'outputs');
    const bad = issues.some(
      (issue) => issue.wire && `${issue.wire.from}->${issue.wire.to}` === edge.id,
    );
    return {
      ...edge,
      data: { type, bad },
      className: `wire-${type ?? 'unknown'}`,
      style: {
        stroke: bad ? wireStrokes.bad : type ? pinLooks[type].stroke : wireStrokes.unknown,
        strokeWidth: 2,
      },
    };
  });
}

export interface GraphCanvasProps {
  readonly document: GraphDocument;
  readonly catalog: readonly NodeKind[];
  readonly rules: DocumentRules;
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  readonly selected: string | undefined;
  readonly onChange: (document: GraphDocument, urgent?: boolean) => void;
  readonly onSelect: (node: string | undefined) => void;
  readonly onInit?: (instance: ReactFlowInstance<GraphFlowNode, GraphFlowEdge>) => void;
}

/**
 * The canvas of the editor (spec signal-constructor «Правка графа на холсте»): nodes with a handle per pin, wires only
 * between pins of one type, a new wire on a wired input replaces the old one, Delete takes the selected nodes and
 * wires away, a kind dropped from the palette becomes a node where it falls.
 */
export function GraphCanvas({
  document,
  catalog,
  rules,
  issues,
  readOnly,
  selected,
  onChange,
  onSelect,
  onInit,
}: GraphCanvasProps) {
  const flow = useMemo(() => toFlow(document, catalog), [document, catalog]);
  const [instance, setInstance] = useState<ReactFlowInstance<GraphFlowNode, GraphFlowEdge>>();
  // Places while dragging live here; the document changes once the drag ends.
  const [nodes, setNodes] = useState(flow.nodes);
  useEffect(
    () => setNodes(flow.nodes.map((node) => ({ ...node, selected: node.id === selected }))),
    [flow.nodes, selected],
  );

  const colored = useMemo(
    () => coloredWires(flow.edges, document, catalog, issues),
    [flow.edges, document, catalog, issues],
  );
  const [edges, setEdges] = useState(colored);
  useEffect(() => setEdges(colored), [colored]);

  const environment = useMemo(
    (): GraphEnvironment => ({
      graph: document.graph,
      catalog,
      issues,
      readOnly,
      connected: new Set(document.graph.wires.flatMap((wire) => [wire.from, wire.to])),
      setValue: (node, pin, value) => onChange(setInputValue(document, node, pin, value)),
    }),
    [document, catalog, issues, readOnly, onChange],
  );

  const reference = (connection: Connection | GraphFlowEdge) => wireOf(connection);

  function drop(event: DragEvent) {
    const signal = event.dataTransfer.getData(signalMime);
    if (signal !== '' && instance && !readOnly) {
      event.preventDefault();
      const position = instance.screenToFlowPosition({ x: event.clientX, y: event.clientY });
      const added = addSignalNode(document, catalog, JSON.parse(signal) as SignalEntry, position);
      onChange(added.document, true);
      onSelect(added.id);
      return;
    }

    const name = event.dataTransfer.getData(paletteMime);
    const kind = catalog.find((candidate) => candidate.kind === name);
    if (!kind || !instance || readOnly) {
      return;
    }

    event.preventDefault();
    const position = instance.screenToFlowPosition({ x: event.clientX, y: event.clientY });
    const added = addNode(document, catalog, kind, position);
    onChange(added.document);
    onSelect(added.id);
  }

  return (
    <GraphEnvironmentContext.Provider value={environment}>
      <div
        className="absolute inset-0"
        onDragOver={(event) => event.preventDefault()}
        onDrop={drop}
      >
        <ReactFlow<GraphFlowNode, GraphFlowEdge>
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          colorMode="dark"
          className="bg-canvas!"
          minZoom={0.3}
          maxZoom={2}
          fitView
          fitViewOptions={fitOptions}
          nodesDraggable={!readOnly}
          nodesConnectable={!readOnly}
          deleteKeyCode={readOnly ? null : ['Delete', 'Backspace']}
          zoomOnDoubleClick={false}
          proOptions={{ hideAttribution: true }}
          onInit={(created) => {
            setInstance(created);
            onInit?.(created);
          }}
          onNodesChange={(changes: NodeChange<GraphFlowNode>[]) =>
            setNodes((current) => applyNodeChanges(changes, current))
          }
          onEdgesChange={(changes: EdgeChange<GraphFlowEdge>[]) =>
            setEdges((current) => applyEdgeChanges(changes, current))
          }
          onNodeDragStop={(_, __, dragged) =>
            onChange(
              moveNodes(
                document,
                catalog,
                Object.fromEntries(
                  dragged.map((node) => [node.id, { x: node.position.x, y: node.position.y }]),
                ),
              ),
            )
          }
          onNodeClick={(_, node) => onSelect(node.id)}
          onPaneClick={() => onSelect(undefined)}
          isValidConnection={(connection) => {
            const { from, to } = reference(connection);
            return canConnect(document.graph, catalog, from, to);
          }}
          onConnect={(connection) => {
            const { from, to } = reference(connection);
            onChange(connect(document, from, to));
          }}
          onBeforeDelete={async ({ nodes: deleting, edges: deletingEdges }) => ({
            nodes: deleting.filter((node) => rules.canRemove(node.data.node)),
            edges: deletingEdges,
          })}
          // One change for everything Delete takes: separate handlers would each start from the same document.
          onDelete={({ nodes: deletedNodes, edges: deletedEdges }) => {
            const withoutWires = deletedEdges.reduce((current, edge) => {
              const { from, to } = reference(edge);
              return disconnect(current, from, to);
            }, document);
            onChange(
              deletedNodes.reduce(
                (current, node) => removeNode(current, catalog, node.id),
                withoutWires,
              ),
            );
            onSelect(undefined);
          }}
        >
          <Background
            variant={BackgroundVariant.Dots}
            color="var(--color-line)"
            gap={24}
            size={1.6}
          />
          <Panel position="bottom-left" className="m-4!">
            <Legend />
          </Panel>
          <Panel position="bottom-right" className="m-4!">
            <ZoomBar />
          </Panel>
        </ReactFlow>
      </div>
    </GraphEnvironmentContext.Provider>
  );
}

const barClass =
  'flex h-11 items-center rounded-[10px] border border-line bg-panel/95 text-xs shadow-xl';

/** The legend of the canvas: the four types of a value, each with the color of its pins and wires. */
function Legend() {
  return (
    <ul aria-label="Типы значений" className={`${barClass} gap-4 px-3.5 text-muted`}>
      {pinTypes.map((type) => (
        <li key={type} data-type={type} className="flex items-center gap-1.5">
          <span aria-hidden="true" className={`size-2 rounded-full ${pinLooks[type].dot}`} />
          {pinLooks[type].word}
        </li>
      ))}
    </ul>
  );
}

const zoomButton =
  'grid h-full w-9 cursor-pointer place-items-center text-sm text-muted hover:text-text';

/** The scale of the canvas: smaller, the scale in percent, larger, and the whole graph in view. */
function ZoomBar() {
  const flow = useReactFlow();
  const { zoom } = useViewport();
  return (
    <div role="toolbar" aria-label="Масштаб" className={`${barClass} px-1`}>
      <button
        type="button"
        aria-label="Уменьшить"
        className={zoomButton}
        onClick={() => void flow.zoomOut()}
      >
        −
      </button>
      <span className="w-12 text-center font-mono text-muted">{Math.round(zoom * 100)}%</span>
      <button
        type="button"
        aria-label="Увеличить"
        className={zoomButton}
        onClick={() => void flow.zoomIn()}
      >
        +
      </button>
      <span aria-hidden="true" className="mx-1 h-5 w-px bg-line" />
      <button
        type="button"
        aria-label="Вписать"
        className={zoomButton}
        onClick={() => void flow.fitView(fitOptions)}
      >
        ⤢
      </button>
    </div>
  );
}
