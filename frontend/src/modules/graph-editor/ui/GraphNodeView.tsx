import { Handle, type NodeProps, Position } from '@xyflow/react';

import type { GraphFlowNode } from '../model/flow';
import { widthOf } from '../model/layout';
import { kindOf, pinsOf } from '../model/pins';
import { referenceOf, signalInfo } from '../model/signals';
import { type Pin, wireOnly } from '../model/types';
import { formatValue, parseValue } from '../model/values';
import { useGraphEnvironment } from './GraphContext';
import { groupFill, groupOf, pinLooks } from './looks';

/**
 * A node on the canvas (spec signal-constructor «Цвет группы узла и типа пина»): a header of the color of its group
 * with its title and id, its settings, and a row per pin with a handle for wires. A signal node goes by the name of
 * its signal and wears the badge of its version.
 */
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
  const newer =
    signal?.latestVersion != null && reference && signal.latestVersion > reference.version
      ? signal.latestVersion
      : undefined;
  const loading = node.kind === 'signal' && signal?.parameters === undefined;
  const title =
    node.kind === 'signal'
      ? (signal?.name ?? kind?.title ?? node.kind)
      : (kind?.title ?? node.kind);
  const rows = Math.max(pins.inputs.length, pins.outputs.length, 1);
  const settings =
    node.kind === 'signal'
      ? [
          ['версия', String(reference?.version ?? '—')],
          ...(newer === undefined ? [] : [['есть версия', String(newer)]]),
          ...(loading ? [['пины', 'загрузка']] : []),
        ]
      : Object.entries(node.settings ?? {});
  const line = settings.map(([name, value]) => `${name}: ${value}`).join(', ');
  const group = groupOf(node.kind, kind?.group);
  const outline =
    issues.length > 0
      ? 'border-2 border-err'
      : selected
        ? 'border-2 border-accent'
        : 'border border-node-line';

  return (
    <div
      role="group"
      aria-label={`${title} ${id}`}
      aria-invalid={issues.length > 0}
      className={`relative rounded-lg bg-node text-xs text-text shadow-xl ${outline}`}
      style={{ width: widthOf(node) }}
    >
      <div
        data-group={group}
        className={`flex items-center justify-between gap-2 rounded-t-[7px] px-2.5 py-1.5 font-semibold ${groupFill(group)}`}
      >
        <span className="flex min-w-0 items-center gap-1.5">
          <span className="truncate">{title}</span>
          {node.kind === 'signal' && (
            <span
              aria-hidden="true"
              className="shrink-0 rounded-[10px] bg-on-accent/60 px-1.5 py-px text-[10px] leading-4 font-medium"
            >
              сигнал v{reference?.version ?? '—'}
            </span>
          )}
        </span>
        <span className="truncate font-mono text-[11px] font-normal opacity-70">{id}</span>
      </div>
      {issues.length > 0 && (
        <span
          aria-hidden="true"
          className="absolute -top-2 -right-2 grid size-[18px] place-items-center rounded-full bg-err text-[11px] font-extrabold text-text"
        >
          !
        </span>
      )}
      {/* The version line of a signal node is read aloud and found by the tests; the eye has the badge of the header. */}
      {line !== '' && (
        <div
          className={
            node.kind === 'signal' && newer === undefined && !loading
              ? 'sr-only'
              : `mx-2 mt-1 truncate rounded bg-canvas/60 px-1.5 py-0.5 font-mono text-[11px] ${newer === undefined ? 'text-muted' : 'text-warn'}`
          }
        >
          {line}
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
                <PinHandle
                  side="source"
                  pin={pins.outputs[index]}
                  connected={environment.connected.has(`${id}.${pins.outputs[index].name}`)}
                  connectable={!environment.readOnly}
                />
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * An input pin: its handle, its name and, without a wire, the field of its value. An event, an order and the position
 * come by a wire only and have no field.
 */
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
      className={`flex min-w-0 items-center gap-2 ${bad ? 'font-semibold text-err' : ''}`}
      data-bad={bad}
    >
      <PinHandle
        side="target"
        pin={pin}
        connected={connected}
        connectable={!environment.readOnly}
        bad={bad}
      />
      <span className="truncate" title={pin.name}>
        {pin.name}
      </span>
      {!connected && !wireOnly(pin.type) && (
        <input
          aria-label={`Значение ${node}.${pin.name}`}
          className="nodrag h-[18px] w-16 shrink-0 rounded border border-node-line bg-canvas/60 px-1 font-mono text-[11px] text-text outline-none placeholder:text-faint focus:border-accent"
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

/**
 * A pin: a circle of 10 px of the color of its type, filled when a wire is on it and an outline when not. The type and
 * whether it is connected are told by `data-type` and `data-connected`.
 */
function PinHandle({
  side,
  pin,
  connected,
  connectable,
  bad = false,
}: {
  readonly side: 'source' | 'target';
  readonly pin: Pin;
  readonly connected: boolean;
  readonly connectable: boolean;
  readonly bad?: boolean;
}) {
  const look = pinLooks[pin.type];
  return (
    <Handle
      type={side}
      id={pin.name}
      position={side === 'source' ? Position.Right : Position.Left}
      isConnectable={connectable}
      data-type={pin.type}
      data-connected={connected}
      className={`size-2.5! border-2! ${bad ? 'border-err!' : look.border} ${
        connected ? (bad ? 'bg-err!' : look.fill) : 'bg-node!'
      }`}
    />
  );
}
