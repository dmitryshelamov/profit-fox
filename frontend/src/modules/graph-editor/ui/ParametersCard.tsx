import { useState } from 'react';

import { Button } from '@/shared/ui/Button';
import { SelectField } from '@/shared/ui/SelectField';
import { TextField } from '@/shared/ui/TextField';

import { setParameters, setSignalOutputType } from '../model/operations';
import type { GraphDocument, GraphParameter, PinType } from '../model/types';
import { formatValue, parseValue } from '../model/values';
import { Card, FieldLabel } from './Card';

const types: readonly PinType[] = ['number', 'boolean', 'duration'];

const startValue: Record<PinType, GraphParameter['value']> = {
  number: 0,
  boolean: false,
  duration: '1h',
  event: 0,
  order: 0,
  position: 0,
};

/**
 * The parameters of the graph (spec signal-constructor «Параметры графа»): name, type, value, and for a number the
 * bounds. The outputs of every `parameters` node follow the list; a renamed parameter keeps its wires. A signal also
 * chooses here the type of its output: an event, or a boolean — a state (change add-ideas-and-market).
 */
export function ParametersCard({
  document,
  readOnly,
  signal = false,
  onChange,
  onClose,
}: {
  readonly document: GraphDocument;
  readonly readOnly: boolean;
  readonly signal?: boolean;
  readonly onChange: (document: GraphDocument) => void;
  readonly onClose: () => void;
}) {
  const parameters = document.graph.parameters;
  const [name, setName] = useState('');
  const taken = parameters.some((parameter) => parameter.name === name);

  const change = (index: number, parameter: GraphParameter) => {
    const before = parameters[index]!;
    const next = parameters.map((current, at) => (at === index ? parameter : current));
    onChange(
      setParameters(
        document,
        next,
        before.name !== parameter.name ? { [before.name]: parameter.name } : {},
      ),
    );
  };

  const output = signal ? document.graph.outputs[0] : undefined;
  return (
    <Card title="Параметры" onClose={onClose}>
      {output && (
        <label className="flex flex-col gap-1.5">
          <FieldLabel>Выход сигнала {output.name}</FieldLabel>
          <SelectField
            aria-label="Тип выхода сигнала"
            mono
            className="w-full"
            value={output.type}
            disabled={readOnly}
            onChange={(event) =>
              onChange(setSignalOutputType(document, event.target.value as 'event' | 'boolean'))
            }
          >
            <option value="event">event</option>
            <option value="boolean">boolean</option>
          </SelectField>
          <span className="text-xs text-muted">
            event — когда произошло; boolean — состояние, истинное, пока условие выполнено.
          </span>
        </label>
      )}
      {parameters.length === 0 && <p className="text-muted">Параметров нет.</p>}
      {parameters.map((parameter, index) => (
        <fieldset
          key={index}
          aria-label={`Параметр ${parameter.name}`}
          className="flex min-w-0 flex-col gap-1.5 rounded-lg border border-line p-2.5"
        >
          <TextField
            aria-label="Имя параметра"
            mono
            defaultValue={parameter.name}
            key={parameter.name}
            readOnly={readOnly}
            onBlur={(event) => {
              const renamed = event.target.value.trim();
              if (
                renamed !== '' &&
                renamed !== parameter.name &&
                !parameters.some((other) => other.name === renamed)
              ) {
                change(index, { ...parameter, name: renamed });
              }
            }}
          />
          <SelectField
            aria-label="Тип параметра"
            mono
            className="w-full"
            value={parameter.type}
            disabled={readOnly}
            onChange={(event) => {
              const type = event.target.value as PinType;
              change(index, { name: parameter.name, type, value: startValue[type] });
            }}
          >
            {types.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </SelectField>
          <ValueField
            label="Значение параметра"
            value={parameter.value}
            type={parameter.type}
            readOnly={readOnly}
            onValue={(value) => value !== undefined && change(index, { ...parameter, value })}
          />
          {parameter.type === 'number' && (
            <span className="flex gap-2">
              <BoundField
                label="Минимум"
                value={parameter.minimum}
                readOnly={readOnly}
                onValue={(minimum) => change(index, withBound(parameter, 'minimum', minimum))}
              />
              <BoundField
                label="Максимум"
                value={parameter.maximum}
                readOnly={readOnly}
                onValue={(maximum) => change(index, withBound(parameter, 'maximum', maximum))}
              />
            </span>
          )}
          {!readOnly && (
            <Button
              variant="danger"
              size="sm"
              className="self-start"
              onClick={() =>
                onChange(
                  setParameters(
                    document,
                    parameters.filter((_, at) => at !== index),
                  ),
                )
              }
            >
              Удалить параметр
            </Button>
          )}
        </fieldset>
      ))}
      {!readOnly && (
        <form
          className="flex flex-col gap-1.5"
          onSubmit={(event) => {
            event.preventDefault();
            if (name !== '' && !taken) {
              onChange(
                setParameters(document, [...parameters, { name, type: 'number', value: 0 }]),
              );
              setName('');
            }
          }}
        >
          <FieldLabel>Новый параметр</FieldLabel>
          <span className="flex gap-2">
            <TextField
              aria-label="Имя нового параметра"
              mono
              className="min-w-0 flex-1"
              placeholder="threshold"
              value={name}
              onChange={(event) => setName(event.target.value.trim())}
            />
            <Button type="submit" disabled={name === '' || taken}>
              Добавить
            </Button>
          </span>
        </form>
      )}
    </Card>
  );
}

function withBound(
  parameter: GraphParameter,
  bound: 'minimum' | 'maximum',
  value: number | undefined,
): GraphParameter {
  const next: Record<string, unknown> = { ...parameter };
  if (value === undefined) {
    delete next[bound];
  } else {
    next[bound] = value;
  }

  return next as unknown as GraphParameter;
}

function ValueField({
  label,
  value,
  type,
  readOnly,
  onValue,
}: {
  readonly label: string;
  readonly value: GraphParameter['value'];
  readonly type: PinType;
  readonly readOnly: boolean;
  readonly onValue: (value: GraphParameter['value'] | undefined) => void;
}) {
  return (
    <TextField
      aria-label={label}
      mono
      defaultValue={formatValue(value)}
      key={`${type}:${formatValue(value)}`}
      readOnly={readOnly}
      onBlur={(event) => {
        const parsed = parseValue(event.target.value, type);
        if (parsed !== value) {
          onValue(parsed);
        }
      }}
    />
  );
}

function BoundField({
  label,
  value,
  readOnly,
  onValue,
}: {
  readonly label: string;
  readonly value: number | undefined;
  readonly readOnly: boolean;
  readonly onValue: (value: number | undefined) => void;
}) {
  return (
    <TextField
      aria-label={label}
      mono
      placeholder={label.toLowerCase()}
      className="min-w-0 flex-1"
      defaultValue={value === undefined ? '' : String(value)}
      key={String(value)}
      readOnly={readOnly}
      onBlur={(event) => {
        const parsed = parseValue(event.target.value, 'number');
        const bound = typeof parsed === 'number' ? parsed : undefined;
        if (bound !== value) {
          onValue(bound);
        }
      }}
    />
  );
}
