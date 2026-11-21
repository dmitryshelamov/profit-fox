import { Handle, type NodeProps, Position } from '@xyflow/react';

import type { GraphFlowNode } from '../model/flow';
import { nodeWidth } from '../model/layout';
import { kindOf, pinsOf } from '../model/pins';
import { referenceOf, signalInfo } from '../model/signals';
import type { Pin } from '../model/types';
import { formatValue, parseValue } from '../model/values';
import { useGraphEnvironment } from './GraphContext';
import { headerColor, pinColors } from './pinColors';

/** A node on the canvas: a header with its title and id, its settings, and a row per pin with a handle for wires. */
export function GraphNodeView({ id, data, selected }: NodeProps<GraphFlowNode>) {
  const environment = useGraphEnvironment();
  const { node } = data;
  const kind = kindOf(node, environment.catalog);
  const pins = pinsOf(node, environment.catalog, environment.graph);
  const issues = environment.issues.filter((issue) => issue.node === id);
  const badPins = new Set(issues.map((issue) => issue.pin));
  // A signal node goes by the name of its signal and shows its version, not the id it holds.
  const signal = node.kind === 'signal' ? signalInfo(node, environment.catalog) : undefined;
  const reference = referenceOf(node);
  const title =
    node.kind === 'signal'
      ? (signal?.name ?? kind?.title ?? node.kind)
      : (kind?.title ?? node.kind);
  const rows = Math.max(pins.inputs.length, pins.outputs.length, 1);
  const settings =
    node.kind === 'signal'
      ? [
          ['версия', String(reference?.version ?? '—')],
          ...(signal?.latestVersion != null && reference && signal.latestVersion > reference.version
            ? [['есть версия', String(signal.latestVersion)]]
            : []),
          ...(signal?.parameters === undefined ? [['пины', 'загрузка']] : []),
        ]
      : Object.entries(node.settings ?? {});
  const outline =
    issues.length > 0
      ? 'border-2 border-red-500'
      : selected
        ? 'border-2 border-amber-400'
        : 'border border-zinc-700';

  return (
    <div
      role="group"
      aria-label={`${title} ${id}`}
      aria-invalid={issues.length > 0}
      className={`relative rounded-lg bg-zinc-900 text-xs text-zinc-200 shadow-xl ${outline}`}
      style={{ width: nodeWidth }}
    >
      <div
        className="flex items-center justify-between gap-2 rounded-t-[7px] px-2.5 py-1.5 font-semibold text-white"
        style={{ background: headerColor(kind?.group) }}
      >
        <span className="truncate">{title}</span>
        <span className="truncate font-mono text-[11px] font-normal text-white/70">{id}</span>
      </div>
      {issues.length > 0 && (
        <span
          aria-hidden="true"
          className="absolute -top-2 -right-2 grid size-[18px] place-items-center rounded-full bg-red-500 text-[11px] font-extrabold text-white"
        >
          !
        </span>
      )}
      {settings.length > 0 && (
        <div className="mx-2 mt-1 truncate rounded bg-zinc-800 px-1.5 py-0.5 text-[11px] text-zinc-400">
          {settings.map(([name, value]) => `${name}: ${value}`).join(', ')}
        </div>
      )}
      <div className="py-1">
        {Array.from({ length: rows }, (_, index) => (
          <div key={index} className="relative flex h-6 items-center justify-between gap-2 px-3">
            {pins.inputs[index] ? (
              <Input
                node={id}
                pin={pins.inputs[index]}
                bad={badPins.has(pins.inputs[index].name)}
              />
            ) : (
              <span />
            )}
            {pins.outputs[index] && (
              <span className="flex min-w-0 items-center">
                <span className="truncate" title={pins.outputs[index].name}>
                  {pins.outputs[index].name}
                </span>
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

/** An input pin: its handle, its name and, without a wire, the field of its value. An event takes no value. */
function Input({
  node,
  pin,
  bad,
}: {
  readonly node: string;
  readonly pin: Pin;
  readonly bad: boolean;
}) {
  const environment = useGraphEnvironment();
  const connected = environment.connected.has(`${node}.${pin.name}`);
  const value = environment.graph.nodes.find((candidate) => candidate.id === node)?.inputs?.[
    pin.name
  ];

  return (
    <span
      className={`flex min-w-0 items-center gap-2 ${bad ? 'font-semibold text-red-400' : ''}`}
      data-bad={bad}
    >
      <Handle
        type="target"
        id={pin.name}
        position={Position.Left}
        isConnectable={!environment.readOnly}
        style={pinStyle(pin, connected, bad)}
      />
      <span className="truncate" title={pin.name}>
        {pin.name}
      </span>
      {!connected && pin.type !== 'event' && (
        <input
          aria-label={`Значение ${node}.${pin.name}`}
          className="nodrag h-[18px] w-16 shrink-0 rounded border border-zinc-700 bg-zinc-800 px-1 font-mono text-[11px] text-zinc-100"
          defaultValue={formatValue(value)}
          key={formatValue(value)}
          placeholder={pin.fallback === undefined ? undefined : formatValue(pin.fallback)}
          readOnly={environment.readOnly}
          onBlur={(event) => {
            const parsed = parseValue(event.target.value, pin.type);
            if (parsed !== value) {
              environment.setValue(node, pin.name, parsed);
            }
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.currentTarget.blur();
            }
          }}
        />
      )}
    </span>
  );
}

function pinStyle(pin: Pin, connected: boolean, bad = false) {
  const color = bad ? '#ef4444' : pinColors[pin.type];
  return {
    width: 10,
    height: 10,
    border: `2px solid ${color}`,
    background: connected ? color : '#18181b',
  };
}
