import { renamePin, setParameters } from '../model/operations';
import type { CatalogNode, ParameterDef, PinType, StrategyDef } from '../model/types';
import { Card, Field, Segmented } from './Card';
import { formatValue, parseValue } from './values';

const input = 'h-7 w-full rounded border border-line bg-app px-2 font-mono text-xs';

const types: readonly { value: PinType; title: string }[] = [
  { value: 'number', title: 'число' },
  { value: 'boolean', title: 'логическое' },
  { value: 'duration', title: 'длительность' },
];

/** The name, the direction and the parameters of the strategy. */
export function StrategyCard({
  name,
  definition,
  catalog,
  readOnly,
  onName,
  onChange,
  onClose,
}: {
  name: string;
  definition: StrategyDef;
  catalog: readonly CatalogNode[];
  readOnly: boolean;
  onName: (name: string) => void;
  onChange: (definition: StrategyDef) => void;
  onClose: () => void;
}) {
  const parameters = definition.parameters ?? [];
  const replace = (index: number, parameter: ParameterDef) =>
    onChange(
      setParameters(
        definition,
        catalog,
        parameters.map((item, at) => (at === index ? parameter : item)),
      ),
    );

  function add() {
    const taken = new Set(parameters.map((parameter) => parameter.name));
    let number = 1;

    while (taken.has(`param${number}`)) {
      number++;
    }

    onChange(
      setParameters(definition, catalog, [
        ...parameters,
        { name: `param${number}`, type: 'number', default: 0 },
      ]),
    );
  }

  return (
    <Card label="Стратегия" title="Стратегия" onClose={onClose}>
      <Field label="Название">
        <input
          aria-label="Название"
          value={name}
          readOnly={readOnly}
          onChange={(event) => onName(event.target.value)}
          className="h-8 w-full rounded-md border border-line bg-panel-2 px-2.5 text-sm"
        />
      </Field>
      <Field label="Направление">
        <Segmented
          label="Направление"
          disabled={readOnly}
          value={definition.direction}
          options={[
            { value: 'long', title: 'Лонг' },
            { value: 'short', title: 'Шорт' },
          ]}
          onChange={(direction) =>
            onChange({ ...definition, direction: direction as StrategyDef['direction'] })
          }
        />
      </Field>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <h3 className="text-[13.5px] font-semibold">Параметры</h3>
          {!readOnly && (
            <button type="button" onClick={add} className="text-[12.5px] font-semibold text-accent">
              + Добавить
            </button>
          )}
        </div>
        {parameters.map((parameter, index) => (
          <div key={index} className="space-y-1.5 rounded-md border border-line bg-panel-2 p-2.5">
            <div className="flex gap-1.5">
              <input
                aria-label="Имя параметра"
                defaultValue={parameter.name}
                key={parameter.name}
                readOnly={readOnly}
                onBlur={(event) => {
                  const renamed = event.target.value.trim();

                  if (renamed && renamed !== parameter.name) {
                    onChange(renamePin(definition, catalog, 'parameters', parameter.name, renamed));
                  }
                }}
                className={`${input} font-semibold`}
              />
              <select
                aria-label={`Тип ${parameter.name}`}
                value={parameter.type}
                disabled={readOnly}
                onChange={(event) =>
                  replace(index, { name: parameter.name, type: event.target.value as PinType })
                }
                className={`${input} w-28`}
              >
                {types.map((type) => (
                  <option key={type.value} value={type.value}>
                    {type.title}
                  </option>
                ))}
              </select>
              {!readOnly && (
                <button
                  type="button"
                  aria-label={`Удалить параметр ${parameter.name}`}
                  onClick={() =>
                    onChange(
                      setParameters(
                        definition,
                        catalog,
                        parameters.filter((_, at) => at !== index),
                      ),
                    )
                  }
                  className="px-1 text-faint hover:text-err"
                >
                  ×
                </button>
              )}
            </div>
            <div className="grid grid-cols-3 gap-1.5">
              {(['default', 'min', 'max'] as const)
                .filter((field) => field === 'default' || parameter.type !== 'boolean')
                .map((field) => (
                  <label key={field} className="space-y-0.5 text-[10.5px] text-faint">
                    {{ default: 'по умолчанию', min: 'мин', max: 'макс' }[field]}
                    <input
                      aria-label={`${field} ${parameter.name}`}
                      defaultValue={formatValue(parameter[field])}
                      key={formatValue(parameter[field])}
                      readOnly={readOnly}
                      onBlur={(event) => {
                        const value = parseValue(event.target.value, parameter.type);
                        const { [field]: _, ...rest } = parameter;
                        replace(index, value === undefined ? rest : { ...rest, [field]: value });
                      }}
                      className={input}
                    />
                  </label>
                ))}
            </div>
            {parameter.type === 'number' && (
              <label className="flex items-center gap-1.5 text-[11.5px] text-muted">
                <input
                  type="checkbox"
                  checked={parameter.integer ?? false}
                  disabled={readOnly}
                  onChange={(event) => {
                    const { integer: _, ...rest } = parameter;
                    replace(index, event.target.checked ? { ...rest, integer: true } : rest);
                  }}
                />
                целое
              </label>
            )}
          </div>
        ))}
        <p className="text-[11.5px] text-faint">
          Каждый параметр — выход узла «Параметры». Значения задают бот и бэктест.
        </p>
      </div>
    </Card>
  );
}
