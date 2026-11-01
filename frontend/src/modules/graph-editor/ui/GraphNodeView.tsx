import { Handle, type NodeProps, Position } from '@xyflow/react';

import type { GraphFlowNode } from '../model/flow';
import { nodeWidth } from '../model/layout';
import { pinsOf } from '../model/pins';
import type { Pin } from '../model/types';
import { useGraphEnvironment } from './GraphContext';
import { colorOf, settingsSummary } from './looks';
import { formatValue, parseValue } from './values';

const pinColors = {
  number: 'var(--color-pin-number)',
  boolean: 'var(--color-pin-boolean)',
  duration: 'var(--color-pin-duration)',
};

export function GraphNodeView({ id, data, selected }: NodeProps<GraphFlowNode>) {
  const environment = useGraphEnvironment();
  const { node } = data;
  const entry = environment.catalog.find(
    (candidate) => candidate.id === (node.kind === 'composite' ? node.ref : node.kind),
  );
  const pins = pinsOf(node, environment.catalog, environment.context);
  const issues = environment.issues.filter((issue) => issue.nodeId === id);
  const badPins = new Set(issues.map((issue) => issue.pin));
  const title = entry?.title ?? node.ref ?? node.kind;
  const summary = settingsSummary(node.settings);
  const rows = Math.max(pins.inputs.length, pins.outputs.length);
  const outline =
    issues.length > 0
      ? 'border-err border-2'
      : selected
        ? 'border-accent border-2'
        : 'border-node-line';

  return (
    <div
      role="group"
      aria-label={title}
      aria-invalid={issues.length > 0}
      className={`relative rounded-lg border bg-node text-xs shadow-xl ${outline}`}
      style={{ width: nodeWidth }}
    >
      <div
        className="flex items-center justify-between gap-3 rounded-t-[7px] px-2.5 py-1.5 text-[12.5px] font-semibold text-white"
        style={{ background: colorOf(node.kind) }}
      >
        <span>{title}</span>
        {node.kind === 'composite' && <span aria-label="составной узел">◆</span>}
      </div>
      {issues.length > 0 && (
        <span
          aria-hidden="true"
          className="absolute -top-2 -right-2 grid size-[18px] place-items-center rounded-full bg-err text-[11px] font-extrabold text-white"
        >
          !
        </span>
      )}
      {summary && (
        <div className="mx-2 mt-1 rounded bg-[#1c1f26] px-1.5 py-0.5 text-[11px] text-muted">
          {summary}
        </div>
      )}
      <div className="py-1">
        {Array.from({ length: rows }, (_, index) => (
          <div
            key={index}
            className="relative flex h-[22px] items-center justify-between gap-3 px-3"
          >
            {pins.inputs[index] ? (
              <Input
                nodeId={id}
                pin={pins.inputs[index]}
                bad={badPins.has(pins.inputs[index].name)}
              />
            ) : (
              <span />
            )}
            {pins.outputs[index] && (
              <span className="flex min-w-0">
                <PinName name={pins.outputs[index].name} />
                <Handle
                  type="source"
                  id={pins.outputs[index].name}
                  position={Position.Right}
                  isConnectable={!environment.readOnly}
                  style={pinStyle(
                    pins.outputs[index],
                    environment.connected.has(`${id}.${pins.outputs[index].name}`),
                  )}
                />
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function Input({ nodeId, pin, bad }: { nodeId: string; pin: Pin; bad: boolean }) {
  const environment = useGraphEnvironment();
  const node = useNodeDef(nodeId);
  const connected = environment.connected.has(`${nodeId}.${pin.name}`);
  const value = node?.inputs?.[pin.name];

  return (
    <span className={`flex min-w-0 items-center gap-2 ${bad ? 'font-semibold text-err' : ''}`}>
      <Handle
        type="target"
        id={pin.name}
        position={Position.Left}
        isConnectable={!environment.readOnly}
        style={pinStyle(pin, connected, bad)}
      />
      <PinName name={pin.name} />
      {!connected && (
        <input
          aria-label={`Значение ${nodeId}.${pin.name}`}
          className="nodrag h-[18px] w-14 shrink-0 rounded border border-line bg-panel-2 px-1 font-mono text-[11px] text-text"
          defaultValue={formatValue(value)}
          key={formatValue(value)}
          readOnly={environment.readOnly}
          onBlur={(event) => {
            const parsed = parseValue(event.target.value, pin.type);

            if (parsed !== value) {
              environment.setValue(nodeId, pin.name, parsed);
            }
          }}
        />
      )}
    </span>
  );
}

/** A pin name cut to the fixed width of the node, in full in its tooltip. */
function PinName({ name }: { name: string }) {
  return (
    <span className="truncate" title={name}>
      {name}
    </span>
  );
}

function useNodeDef(nodeId: string) {
  const environment = useGraphEnvironment();

  return environment.nodes.find((node) => node.id === nodeId);
}

function pinStyle(pin: Pin, connected: boolean, bad = false) {
  const color = bad ? 'var(--color-err)' : pinColors[pin.type];

  return {
    width: 10,
    height: 10,
    border: `2px solid ${color}`,
    background: connected ? color : 'var(--color-node)',
  };
}
