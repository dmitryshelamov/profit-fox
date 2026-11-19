import '@xyflow/react/dist/style.css';

import {
  applyEdgeChanges,
  applyNodeChanges,
  Background,
  BackgroundVariant,
  type Connection,
  Controls,
  type EdgeChange,
  type NodeChange,
  ReactFlow,
  type ReactFlowInstance,
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
import type { GraphDocument, NodeKind } from '../model/types';
import { canConnect } from '../model/wires';
import { type GraphEnvironment, GraphEnvironmentContext } from './GraphContext';
import { GraphNodeView } from './GraphNodeView';
import { pinColors } from './pinColors';

/** The drag-and-drop type of a kind dragged from the palette: its name. */
export const paletteMime = 'application/x-profit-fox-node';

const nodeTypes = { graph: GraphNodeView };

export interface GraphCanvasProps {
  readonly document: GraphDocument;
  readonly catalog: readonly NodeKind[];
  readonly rules: DocumentRules;
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  readonly selected: string | undefined;
  readonly onChange: (document: GraphDocument) => void;
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
    () =>
      flow.edges.map((edge): GraphFlowEdge => {
        const { from } = wireOf(edge);
        const type = typeOf(document.graph, catalog, from, 'outputs');
        const bad = issues.some(
          (issue) => issue.wire && `${issue.wire.from}->${issue.wire.to}` === edge.id,
        );
        return {
          ...edge,
          style: { stroke: bad ? '#ef4444' : type ? pinColors[type] : '#71717a', strokeWidth: 2 },
        };
      }),
    [flow.edges, document.graph, catalog, issues],
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
          <Background variant={BackgroundVariant.Dots} color="#27272a" gap={24} size={1.6} />
          <Controls position="bottom-left" showInteractive={false} />
        </ReactFlow>
      </div>
    </GraphEnvironmentContext.Provider>
  );
}
