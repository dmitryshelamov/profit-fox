import { Link } from '@tanstack/react-router';

import { connect, contextOf, disconnect, removeNode, setSetting } from '../model/operations';
import { pinsOf } from '../model/pins';
import type { AnyDef, CatalogNode } from '../model/types';
import { canConnect } from '../model/wires';
import { Card, Field, Segmented } from './Card';
import { settingNames, settingTitles } from './looks';

const pinColors = {
  number: 'var(--color-pin-number)',
  boolean: 'var(--color-pin-boolean)',
  duration: 'var(--color-pin-duration)',
};

/** The properties of the selected node: its settings and what each input is wired to. */
export function NodeCard<T extends AnyDef>({
  definition,
  catalog,
  nodeId,
  readOnly,
  onChange,
  onClose,
}: {
  definition: T;
  catalog: readonly CatalogNode[];
  nodeId: string;
  readOnly: boolean;
  onChange: (definition: T) => void;
  onClose: () => void;
}) {
  const node = definition.nodes.find((candidate) => candidate.id === nodeId);

  if (!node) {
    return null;
  }

  const context = contextOf(definition);
  const entry = catalog.find(
    (candidate) => candidate.id === (node.kind === 'composite' ? node.ref : node.kind),
  );
  const title = entry?.title ?? node.ref ?? node.kind;
  const pins = pinsOf(node, catalog, context);
  const outputs = definition.nodes
    .filter((other) => other.id !== nodeId)
    .flatMap((other) =>
      pinsOf(other, catalog, context).outputs.map((pin) => `${other.id}.${pin.name}`),
    );

  return (
    <Card
      label={`Узел: ${title}`}
      onClose={onClose}
      title={
        <>
          {title}
          <span className="block font-mono text-xs font-normal text-muted">id: {node.id}</span>
        </>
      }
    >
      {entry?.settings.map((setting) => (
        <Field key={setting.name} label={settingNames[setting.name] ?? setting.name}>
          <Segmented
            label={settingNames[setting.name] ?? setting.name}
            disabled={readOnly}
            value={node.settings?.[setting.name] ?? setting.default}
            options={setting.values.map((value) => ({
              value,
              title: settingTitles[setting.name]?.[value] ?? value,
            }))}
            onChange={(value) =>
              onChange(setSetting(definition, catalog, nodeId, setting.name, value))
            }
          />
        </Field>
      ))}
      {node.kind === 'composite' && node.ref && (
        <Link
          to="/nodes/$id"
          params={{ id: node.ref }}
          className="block text-sm font-semibold text-accent"
        >
          Открыть узел
        </Link>
      )}
      {pins.inputs.length > 0 && (
        <Field label="Входы">
          {pins.inputs.map((pin) => {
            const to = `${nodeId}.${pin.name}`;
            const source = definition.wires?.find((wire) => wire.to === to)?.from ?? '';

            return (
              <label
                key={pin.name}
                className="flex h-8 items-center gap-2 rounded-md border border-line bg-panel-2 px-2.5 text-sm"
              >
                <span
                  className="size-[9px] rounded-full"
                  style={{ background: pinColors[pin.type] }}
                />
                <span className="flex-1 font-mono">{pin.name}</span>
                <select
                  aria-label={`Источник ${pin.name}`}
                  disabled={readOnly}
                  value={source}
                  onChange={(event) => {
                    const from = event.target.value;
                    const without = source ? disconnect(definition, source, to) : definition;
                    onChange(from ? connect(without, from, to) : without);
                  }}
                  className="max-w-36 bg-transparent font-mono text-xs text-muted"
                >
                  <option value="">не подключён</option>
                  {outputs
                    .filter((from) => canConnect(definition, catalog, context, from, to))
                    .map((from) => (
                      <option key={from} value={from}>
                        {from}
                      </option>
                    ))}
                </select>
              </label>
            );
          })}
          <p className="text-[11.5px] text-faint">
            Неподключённому входу значение задаётся прямо на узле.
          </p>
        </Field>
      )}
      {!readOnly && (
        <button
          type="button"
          onClick={() => {
            onChange(removeNode(definition, nodeId));
            onClose();
          }}
          className="text-sm font-semibold text-err"
        >
          Удалить узел
        </button>
      )}
    </Card>
  );
}
