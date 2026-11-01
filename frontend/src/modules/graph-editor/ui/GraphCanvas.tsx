import '@xyflow/react/dist/style.css';

import {
  applyEdgeChanges,
  applyNodeChanges,
  Background,
  BackgroundVariant,
  type Connection,
  type EdgeChange,
  type NodeChange,
  ReactFlow,
  type ReactFlowInstance,
} from '@xyflow/react';
import { type DragEvent, useEffect, useMemo, useRef, useState } from 'react';

import { type GraphFlowEdge, type GraphFlowNode, toFlow } from '../model/flow';
import type { Issue } from '../model/issues';
import {
  addNode,
  connect,
  contextOf,
  disconnect,
  moveNodes,
  removeNode,
  setInputValue,
} from '../model/operations';
import { pinsOf } from '../model/pins';
import type { AnyDef, CatalogNode } from '../model/types';
import { canConnect } from '../model/wires';
import { CanvasChrome } from './CanvasChrome';
import { type GraphEnvironment, GraphEnvironmentContext } from './GraphContext';
import { GraphNodeView } from './GraphNodeView';

/** The drag-and-drop type of a node dragged from the palette: JSON `{ kind, ref? }`. */
export const paletteMime = 'application/x-profit-fox-node';

const nodeTypes = { graph: GraphNodeView };

const pinColors: Record<string, string> = {
  number: 'var(--color-pin-number)',
  boolean: 'var(--color-pin-boolean)',
  duration: 'var(--color-pin-duration)',
};

export interface GraphCanvasProps<T extends AnyDef> {
  readonly definition: T;
  readonly catalog: readonly CatalogNode[];
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  readonly onChange: (definition: T) => void;
  /** Drops the layout of the graph: "Выстроить". */
  readonly onRelayout: () => void;
  readonly onSelect: (nodeId: string | undefined) => void;
  readonly onInit?: (instance: ReactFlowInstance<GraphFlowNode, GraphFlowEdge>) => void;
}

export function GraphCanvas<T extends AnyDef>({
  definition,
  catalog,
  issues,
  readOnly,
  onChange,
  onRelayout,
  onSelect,
  onInit,
}: GraphCanvasProps<T>) {
  const flow = useMemo(() => toFlow(definition, catalog), [definition, catalog]);
  const [nodes, setNodes] = useState(flow.nodes);
  const [instance, setInstance] = useState<ReactFlowInstance<GraphFlowNode, GraphFlowEdge>>();
  const context = useMemo(() => contextOf(definition), [definition]);

  // Positions while dragging live here; the definition changes once the drag ends.
  useEffect(() => setNodes(flow.nodes), [flow.nodes]);

  const colored = useMemo(
    () =>
      flow.edges.map((edge): GraphFlowEdge => {
        const source = definition.nodes.find((node) => node.id === edge.source);
        const type =
          source &&
          pinsOf(source, catalog, context).outputs.find((pin) => pin.name === edge.sourceHandle)
            ?.type;

        return {
          ...edge,
          style: { stroke: pinColors[type ?? ''] ?? 'var(--color-muted)', strokeWidth: 2 },
        };
      }),
    [flow.edges, definition.nodes, catalog, context],
  );
  const reconnected = useRef(false);
  // Selection of wires lives here, like positions of nodes: the definition has no notion of it.
  const [edges, setEdges] = useState(colored);

  useEffect(() => setEdges(colored), [colored]);

  const environment = useMemo(
    (): GraphEnvironment => ({
      nodes: definition.nodes,
      catalog,
      context,
      issues,
      readOnly,
      connected: new Set((definition.wires ?? []).flatMap((wire) => [wire.from, wire.to])),
      setValue: (nodeId, pin, value) => onChange(setInputValue(definition, nodeId, pin, value)),
    }),
    [definition, catalog, context, issues, readOnly, onChange],
  );

  const reference = (connection: Connection | GraphFlowEdge) => ({
    from: `${connection.source}.${connection.sourceHandle}`,
    to: `${connection.target}.${connection.targetHandle}`,
  });

  function drop(event: DragEvent) {
    const type = event.dataTransfer.getData(paletteMime);

    if (!type || !instance || readOnly) {
      return;
    }

    event.preventDefault();
    const position = instance.screenToFlowPosition({ x: event.clientX, y: event.clientY });
    onChange(addNode(definition, catalog, JSON.parse(type), position).definition);
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
          minZoom={0.3}
          maxZoom={2}
          fitView
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
          // Every dragged node keeps its place: a drag can carry a whole selection.
          onNodeDragStop={(_, __, dragged) => onChange(moveNodes(definition, positionsOf(dragged)))}
          onSelectionDragStop={(_, dragged) =>
            onChange(moveNodes(definition, positionsOf(dragged)))
          }
          onNodeClick={(_, node) => onSelect(node.id)}
          onPaneClick={() => onSelect(undefined)}
          isValidConnection={(connection) => {
            const { from, to } = reference(connection);

            return canConnect(definition, catalog, context, from, to);
          }}
          onConnect={(connection) => {
            const { from, to } = reference(connection);
            onChange(connect(definition, from, to));
          }}
          onConnectEnd={(_, state) => {
            // A connected input pin dragged onto empty canvas lets its wire go, like the end of the wire itself.
            if (state.isValid || state.toHandle || state.fromHandle?.type !== 'target') {
              return;
            }

            const to = `${state.fromNode?.id}.${state.fromHandle.id}`;
            const wire = definition.wires?.find((candidate) => candidate.to === to);

            if (wire) {
              onChange(disconnect(definition, wire.from, wire.to));
            }
          }}
          edgesReconnectable={!readOnly}
          onReconnectStart={() => {
            reconnected.current = false;
          }}
          onReconnect={(old, connection) => {
            reconnected.current = true;
            const before = reference(old);
            const after = reference(connection);
            onChange(connect(disconnect(definition, before.from, before.to), after.from, after.to));
          }}
          onReconnectEnd={(_, old) => {
            // A wire end let go over empty canvas takes the wire away, as in Unreal Blueprints.
            if (!reconnected.current) {
              const { from, to } = reference(old);
              onChange(disconnect(definition, from, to));
            }
          }}
          // One change for everything Delete takes: separate node and wire handlers would each start from the same
          // definition, and the later would undo the earlier.
          onDelete={({ nodes: deletedNodes, edges: deletedEdges }) => {
            const withoutWires = deletedEdges.reduce((current, edge) => {
              const { from, to } = reference(edge);

              return disconnect(current, from, to);
            }, definition);

            onChange(
              deletedNodes.reduce((current, node) => removeNode(current, node.id), withoutWires),
            );
          }}
        >
          <Background variant={BackgroundVariant.Dots} color="#2a2f3a" gap={24} size={1.6} />
          <CanvasChrome
            catalog={catalog}
            composite={!('direction' in definition)}
            readOnly={readOnly}
            onAdd={(item, position) =>
              onChange(addNode(definition, catalog, item, position).definition)
            }
            onRelayout={onRelayout}
            onFind={(text) => {
              const found = findNode(definition, catalog, text);
              onSelect(found);

              return found;
            }}
          />
        </ReactFlow>
      </div>
    </GraphEnvironmentContext.Provider>
  );
}

function findNode(
  definition: AnyDef,
  catalog: readonly CatalogNode[],
  text: string,
): string | undefined {
  const needle = text.trim().toLowerCase();

  return definition.nodes.find((node) => {
    const title =
      catalog.find((entry) => entry.id === (node.kind === 'composite' ? node.ref : node.kind))
        ?.title ?? '';

    return node.id.toLowerCase().includes(needle) || title.toLowerCase().includes(needle);
  })?.id;
}

function positionsOf(nodes: readonly GraphFlowNode[]) {
  return Object.fromEntries(
    nodes.map((node) => [node.id, { x: node.position.x, y: node.position.y }]),
  );
}
