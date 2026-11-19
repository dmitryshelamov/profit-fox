import { useState } from 'react';

import type { DocumentRules } from '../document';
import { findNode, wireInto } from '../model/graph';
import type { Issue } from '../model/issues';
import {
  connect,
  disconnect,
  removeNode,
  renameNode,
  setInputValue,
  setSetting,
} from '../model/operations';
import { kindOf, pinsOf } from '../model/pins';
import type { GraphDocument, NodeKind, Pin } from '../model/types';
import { formatValue, parseValue } from '../model/values';
import { canConnect } from '../model/wires';
import { buttonClass, Card, fieldClass } from './Card';

/**
 * The card of the selected node: its id, its settings from the catalog and, for each input, where its value comes
 * from — a wire from an output of the same type or a value written on it.
 */
export function NodeCard({
  document,
  catalog,
  rules,
  issues,
  readOnly,
  node: id,
  onChange,
  onSelect,
}: {
  readonly document: GraphDocument;
  readonly catalog: readonly NodeKind[];
  readonly rules: DocumentRules;
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  readonly node: string;
  readonly onChange: (document: GraphDocument) => void;
  readonly onSelect: (node: string | undefined) => void;
}) {
  const node = findNode(document.graph, id);
  const [name, setName] = useState(id);
  if (node === undefined) {
    return null;
  }

  const kind = kindOf(node, catalog);
  const pins = pinsOf(node, catalog, document.graph);
  const title = kind?.title ?? node.kind;
  const taken = name !== id && document.graph.nodes.some((candidate) => candidate.id === name);
  const own = issues.filter((issue) => issue.node === id);

  return (
    <Card title={`Узел: ${title} ${id}`} onClose={() => onSelect(undefined)}>
      <label className="flex flex-col gap-1">
        Id
        <span className="flex gap-2">
          <input
            aria-label="Id узла"
            className={`${fieldClass} flex-1`}
            value={name}
            readOnly={readOnly}
            onChange={(event) => setName(event.target.value.trim())}
          />
          <button
            type="button"
            className={buttonClass}
            disabled={readOnly || name === id || name === '' || taken}
            onClick={() => {
              onChange(renameNode(document, catalog, id, name));
              onSelect(name);
            }}
          >
            Переименовать
          </button>
        </span>
        {taken && <span className="text-xs text-red-400">Id {name} уже занят.</span>}
      </label>

      {kind?.settings.map((setting) => (
        <label key={setting.name} className="flex flex-col gap-1">
          {setting.name}
          <select
            aria-label={`Настройка ${setting.name}`}
            className={fieldClass}
            value={node.settings?.[setting.name] ?? ''}
            disabled={readOnly}
            onChange={(event) =>
              onChange(setSetting(document, id, setting.name, event.target.value))
            }
          >
            {node.settings?.[setting.name] === undefined && <option value="">—</option>}
            {setting.values.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
      ))}

      {pins.inputs.map((pin) => (
        <InputSource
          key={pin.name}
          document={document}
          catalog={catalog}
          node={id}
          pin={pin}
          readOnly={readOnly}
          onChange={onChange}
        />
      ))}

      {own.length > 0 && (
        <ul aria-label="Ошибки узла" className="space-y-1 text-xs text-red-300">
          {own.map((issue, index) => (
            <li key={index}>
              {issue.place}: {issue.reason}
            </li>
          ))}
        </ul>
      )}

      {!readOnly && rules.canRemove(node) && (
        <button
          type="button"
          className={buttonClass}
          onClick={() => {
            onChange(removeNode(document, catalog, id));
            onSelect(undefined);
          }}
        >
          Удалить узел
        </button>
      )}
    </Card>
  );
}

/** Where an input takes its value: a compatible output, or a value written on it. */
function InputSource({
  document,
  catalog,
  node,
  pin,
  readOnly,
  onChange,
}: {
  readonly document: GraphDocument;
  readonly catalog: readonly NodeKind[];
  readonly node: string;
  readonly pin: Pin;
  readonly readOnly: boolean;
  readonly onChange: (document: GraphDocument) => void;
}) {
  const to = `${node}.${pin.name}`;
  const wire = wireInto(document.graph, node, pin.name);
  const sources = document.graph.nodes.flatMap((candidate) =>
    pinsOf(candidate, catalog, document.graph)
      .outputs.map((output) => `${candidate.id}.${output.name}`)
      .filter((from) => canConnect(document.graph, catalog, from, to)),
  );
  const value = findNode(document.graph, node)?.inputs?.[pin.name];

  return (
    <fieldset className="flex flex-col gap-1">
      <legend>
        {pin.name} <span className="text-xs text-zinc-500">{pin.type}</span>
      </legend>
      <select
        aria-label={`Источник ${pin.name}`}
        className={fieldClass}
        value={wire?.from ?? ''}
        disabled={readOnly}
        onChange={(event) => {
          const from = event.target.value;
          const unwired = wire ? disconnect(document, wire.from, wire.to) : document;
          onChange(from === '' ? unwired : connect(unwired, from, to));
        }}
      >
        <option value="">{pin.type === 'event' ? 'не подключён' : 'значение'}</option>
        {sources.map((from) => (
          <option key={from} value={from}>
            {from}
          </option>
        ))}
      </select>
      {!wire && pin.type !== 'event' && (
        <input
          aria-label={`Значение ${pin.name}`}
          className={fieldClass}
          defaultValue={formatValue(value)}
          key={formatValue(value)}
          readOnly={readOnly}
          onBlur={(event) => {
            const parsed = parseValue(event.target.value, pin.type);
            if (parsed !== value) {
              onChange(setInputValue(document, node, pin.name, parsed));
            }
          }}
        />
      )}
    </fieldset>
  );
}
