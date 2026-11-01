import '@xyflow/react/dist/style.css';

import {
  applyNodeChanges,
  Background,
  BackgroundVariant,
  type Connection,
  type NodeChange,
  ReactFlow,
  type ReactFlowInstance,
} from '@xyflow/react';
import { type DragEvent, useEffect, useMemo, useState } from 'react';

import { type GraphFlowEdge, type GraphFlowNode, toFlow } from '../model/flow';
import type { Issue } from '../model/issues';
import {
  addNode,
  connect,
  contextOf,
  disconnect,
  moveNode,
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
  readonly onSelect: (nodeId: string | undefined) => void;
  readonly onInit?: (instance: ReactFlowInstance<GraphFlowNode, GraphFlowEdge>) => void;
}

export function GraphCanvas<T extends AnyDef>({
  definition,
  catalog,
  issues,
  readOnly,
  onChange,
  onSelect,
  onInit,
}: GraphCanvasProps<T>) {
  const flow = useMemo(() => toFlow(definition), [definition]);
  const [nodes, setNodes] = useState(flow.nodes);
  const [instance, setInstance] = useState<ReactFlowInstance<GraphFlowNode, GraphFlowEdge>>();
  const context = useMemo(() => contextOf(definition), [definition]);

  // Positions while dragging live here; the definition changes once the drag ends.
  useEffect(() => setNodes(flow.nodes), [flow.nodes]);

  const edges = useMemo(
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
          onNodeDragStop={(_, node) => onChange(moveNode(definition, node.id, node.position))}
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
          onNodesDelete={(deleted) =>
            onChange(deleted.reduce((current, node) => removeNode(current, node.id), definition))
          }
          onEdgesDelete={(deleted) =>
            onChange(
              deleted.reduce((current, edge) => {
                const { from, to } = reference(edge);

                return disconnect(current, from, to);
              }, definition),
            )
          }
        >
          <Background variant={BackgroundVariant.Dots} color="#2a2f3a" gap={24} size={1.6} />
          <CanvasChrome
            catalog={catalog}
            composite={!('direction' in definition)}
            readOnly={readOnly}
            onAdd={(item, position) =>
              onChange(addNode(definition, catalog, item, position).definition)
            }
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
