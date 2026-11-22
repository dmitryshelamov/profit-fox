import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import { Button } from '@/shared/ui/Button';
import { Segmented } from '@/shared/ui/Segmented';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

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
import { referenceOf, signalInfo } from '../model/signals';
import { type Catalog, type GraphDocument, type Pin, wireOnly } from '../model/types';
import { formatValue, parseValue } from '../model/values';
import { canConnect } from '../model/wires';
import { Card, FieldLabel } from './Card';
import { groupFill, groupOf, pinLooks } from './looks';

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
  onUpdateSignal,
}: {
  readonly document: GraphDocument;
  readonly catalog: Catalog;
  readonly rules: DocumentRules;
  readonly issues: readonly Issue[];
  readonly readOnly: boolean;
  readonly node: string;
  readonly onChange: (document: GraphDocument) => void;
  readonly onSelect: (node: string | undefined) => void;
  readonly onUpdateSignal?: (node: string) => void;
}) {
  const node = findNode(document.graph, id);
  const [name, setName] = useState(id);
  if (node === undefined) {
    return null;
  }

  const kind = kindOf(node, catalog);
  const pins = pinsOf(node, catalog, document.graph);
  // A signal node goes by the name of its signal, as on the canvas.
  const title =
    (node.kind === 'signal' ? signalInfo(node, catalog)?.name : undefined) ??
    kind?.title ??
    node.kind;
  const taken = name !== id && document.graph.nodes.some((candidate) => candidate.id === name);
  const own = issues.filter((issue) => issue.node === id);

  return (
    <Card
      title={`Узел: ${title} ${id}`}
      heading={
        <>
          <span
            aria-hidden="true"
            className={`size-2.5 shrink-0 rounded-[3px] ${groupFill(groupOf(node.kind, kind?.group))}`}
          />
          <span className="truncate">{title}</span>
          {kind?.group && <span className="text-xs font-normal text-muted">{kind.group}</span>}
        </>
      }
      onClose={() => onSelect(undefined)}
    >
      <label className="flex flex-col gap-1.5">
        <FieldLabel>Id узла</FieldLabel>
        <span className="flex gap-2">
          <TextField
            aria-label="Id узла"
            mono
            className="min-w-0 flex-1"
            value={name}
            readOnly={readOnly}
            onChange={(event) => setName(event.target.value.trim())}
          />
          <Button
            disabled={readOnly || name === id || name === '' || taken}
            onClick={() => {
              onChange(renameNode(document, catalog, id, name));
              onSelect(name);
            }}
          >
            Переименовать
          </Button>
        </span>
        {taken && <span className="text-xs text-err">Id {name} уже занят.</span>}
      </label>

      {node.kind === 'signal' && (
        <SignalOfNode
          node={id}
          reference={referenceOf(node)}
          info={signalInfo(node, catalog)}
          readOnly={readOnly}
          onUpdate={onUpdateSignal}
        />
      )}

      {kind?.settings
        .filter((setting) => setting.values.length > 0)
        .map((setting) => (
          <div key={setting.name} className="flex flex-col gap-1.5">
            <FieldLabel>{setting.name}</FieldLabel>
            {/* A choice of two is in sight as segments; a longer one is a list. */}
            {setting.values.length === 2 ? (
              <Segmented
                label={`Настройка ${setting.name}`}
                className="w-full"
                value={node.settings?.[setting.name]}
                options={setting.values.map((value) => ({ value, label: value }))}
                disabled={readOnly}
                onChange={(value) => onChange(setSetting(document, id, setting.name, value))}
              />
            ) : (
              <SelectField
                aria-label={`Настройка ${setting.name}`}
                mono
                className="w-full"
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
              </SelectField>
            )}
          </div>
        ))}

      {pins.inputs.map((pin) =>
        pin.many ? (
          <ManyInput
            key={pin.name}
            document={document}
            catalog={catalog}
            node={id}
            pin={pin}
            readOnly={readOnly}
            onChange={onChange}
          />
        ) : (
          <InputSource
            key={pin.name}
            document={document}
            catalog={catalog}
            node={id}
            pin={pin}
            readOnly={readOnly}
            onChange={onChange}
          />
        ),
      )}

      {own.length > 0 && (
        <ul
          aria-label="Ошибки узла"
          className="flex flex-col gap-1 rounded-md bg-err-bg px-3 py-2 text-xs text-err"
        >
          {own.map((issue, index) => (
            <li key={index}>
              {issue.place}: {issue.reason}
            </li>
          ))}
        </ul>
      )}

      {!readOnly && rules.canRemove(node) && (
        <div className="-mx-4 mt-1 -mb-4 flex items-center justify-between border-t border-line px-2.5 py-2">
          <Button
            variant="danger"
            onClick={() => {
              onChange(removeNode(document, catalog, id));
              onSelect(undefined);
            }}
          >
            Удалить узел
          </Button>
          <span aria-hidden="true" className="pr-1.5 font-mono text-[11px] text-faint">
            Delete
          </span>
        </div>
      )}
    </Card>
  );
}

/**
 * The signal a node points at (spec strategy-constructor «Узел сигнала на холсте», «Обновление версии сигнала в
 * узле»): its name and version, the update to a newer version, and the way to its editor.
 */
function SignalOfNode({
  node,
  reference,
  info,
  readOnly,
  onUpdate,
}: {
  readonly node: string;
  readonly reference: { readonly signal: string; readonly version: number } | undefined;
  readonly info: ReturnType<typeof signalInfo>;
  readonly readOnly: boolean;
  readonly onUpdate?: (node: string) => void;
}) {
  const latest = info?.latestVersion ?? null;
  return (
    <section aria-label="Сигнал узла" className="flex flex-col gap-1.5">
      <FieldLabel>Сигнал</FieldLabel>
      <span className="flex items-baseline justify-between gap-2">
        <span className="truncate font-mono">
          {info?.name ?? (info ? 'не найден' : 'загрузка')}
        </span>
        <span className="shrink-0 text-xs text-muted">версия {reference?.version ?? '—'}</span>
      </span>
      {reference && latest !== null && latest > reference.version && (
        <Button
          size="sm"
          className="self-start border-warn text-warn"
          disabled={readOnly || onUpdate === undefined}
          onClick={() => onUpdate?.(node)}
        >
          Обновить до версии {latest}
        </Button>
      )}
      {info?.name && (
        <Link
          to="/signals/$name"
          params={{ name: info.name }}
          className="self-start text-xs font-semibold text-accent hover:underline"
        >
          Открыть сигнал
        </Link>
      )}
    </section>
  );
}

/** The name of a pin with the dot and the word of its type, as an input of the card is headed. */
function PinLabel({ pin }: { readonly pin: Pin }) {
  return (
    <FieldLabel>
      {pin.name}
      <span className="flex items-center gap-1.5 font-normal tracking-normal text-faint normal-case">
        <span aria-hidden="true" className={`size-1.5 rounded-full ${pinLooks[pin.type].dot}`} />
        {pinLooks[pin.type].word}
      </span>
    </FieldLabel>
  );
}

/**
 * An input that takes several wires, the orders of the position (spec strategy-constructor «Несколько проводов в позицию
 * на холсте»): the orders wired into it in a list, each with its removal, and a list to add one more.
 */
function ManyInput({
  document,
  catalog,
  node,
  pin,
  readOnly,
  onChange,
}: {
  readonly document: GraphDocument;
  readonly catalog: Catalog;
  readonly node: string;
  readonly pin: Pin;
  readonly readOnly: boolean;
  readonly onChange: (document: GraphDocument) => void;
}) {
  const to = `${node}.${pin.name}`;
  const wired = document.graph.wires.filter((wire) => wire.to === to);
  const sources = document.graph.nodes.flatMap((candidate) =>
    pinsOf(candidate, catalog, document.graph)
      .outputs.map((output) => `${candidate.id}.${output.name}`)
      .filter(
        (from) =>
          canConnect(document.graph, catalog, from, to) &&
          !wired.some((wire) => wire.from === from),
      ),
  );

  return (
    <fieldset className="flex min-w-0 flex-col gap-1.5">
      <legend className="mb-1.5">
        <PinLabel pin={pin} />
      </legend>
      <ul aria-label={`Провода ${pin.name}`} className="flex flex-col gap-1">
        {wired.map((wire) => (
          <li
            key={wire.from}
            className="flex items-center justify-between gap-2 rounded-md bg-panel-2 px-2.5 py-1"
          >
            <span className="truncate font-mono text-xs">{wire.from}</span>
            <Button
              size="sm"
              aria-label={`Убрать ${wire.from}`}
              disabled={readOnly}
              onClick={() => onChange(disconnect(document, wire.from, wire.to))}
            >
              Убрать
            </Button>
          </li>
        ))}
        {wired.length === 0 && <li className="text-xs text-muted">не подключён</li>}
      </ul>
      {!readOnly && sources.length > 0 && (
        <SelectField
          aria-label={`Добавить ${pin.name}`}
          mono
          className="w-full"
          value=""
          onChange={(event) => {
            if (event.target.value !== '') {
              onChange(connect(document, event.target.value, to, catalog));
            }
          }}
        >
          <option value="">добавить провод</option>
          {sources.map((from) => (
            <option key={from} value={from}>
              {from}
            </option>
          ))}
        </SelectField>
      )}
    </fieldset>
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
  readonly catalog: Catalog;
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
    <fieldset className="flex min-w-0 flex-col gap-1.5">
      <legend className="mb-1.5">
        <PinLabel pin={pin} />
      </legend>
      <SelectField
        aria-label={`Источник ${pin.name}`}
        mono
        className="w-full"
        value={wire?.from ?? ''}
        disabled={readOnly}
        onChange={(event) => {
          const from = event.target.value;
          const unwired = wire ? disconnect(document, wire.from, wire.to) : document;
          onChange(from === '' ? unwired : connect(unwired, from, to, catalog));
        }}
      >
        <option value="">{wireOnly(pin.type) ? 'не подключён' : 'значение'}</option>
        {sources.map((from) => (
          <option key={from} value={from}>
            {from}
          </option>
        ))}
      </SelectField>
      {!wire && !wireOnly(pin.type) && (
        <TextField
          aria-label={`Значение ${pin.name}`}
          mono
          defaultValue={formatValue(value)}
          key={formatValue(value)}
          placeholder={pin.fallback === undefined ? undefined : formatValue(pin.fallback)}
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
