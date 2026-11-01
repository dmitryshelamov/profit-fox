import { renamePin, setDeclarations } from '../model/operations';
import type { CatalogNode, CompositeDef, PinDecl, PinType } from '../model/types';
import { Card, Field } from './Card';

const input = 'h-7 w-full rounded border border-line bg-app px-2 font-mono text-xs';

const types: readonly { value: PinType; title: string }[] = [
  { value: 'number', title: 'число' },
  { value: 'boolean', title: 'логическое' },
  { value: 'duration', title: 'длительность' },
];

/** The name of a composite node and the inputs and outputs it declares. */
export function DeclarationsCard({
  name,
  definition,
  catalog,
  readOnly,
  onName,
  onChange,
  onClose,
}: {
  name: string;
  definition: CompositeDef;
  catalog: readonly CatalogNode[];
  readOnly: boolean;
  onName: (name: string) => void;
  onChange: (definition: CompositeDef) => void;
  onClose: () => void;
}) {
  const side = (which: 'inputs' | 'outputs', title: string, one: string) => {
    const pins = definition[which];
    const replace = (next: readonly PinDecl[]) =>
      onChange(setDeclarations(definition, catalog, { ...definition, [which]: next }));

    return (
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <h3 className="text-[13.5px] font-semibold">{title}</h3>
          {!readOnly && (
            <button
              type="button"
              onClick={() => {
                const taken = new Set(pins.map((pin) => pin.name));
                let number = 1;

                while (taken.has(`${which === 'inputs' ? 'input' : 'output'}${number}`)) {
                  number++;
                }

                replace([
                  ...pins,
                  { name: `${which === 'inputs' ? 'input' : 'output'}${number}`, type: 'number' },
                ]);
              }}
              className="text-[12.5px] font-semibold text-accent"
            >
              + Добавить {one}
            </button>
          )}
        </div>
        {pins.map((pin, index) => (
          <div key={index} className="flex gap-1.5 rounded-md border border-line bg-panel-2 p-2">
            <input
              aria-label={`Имя ${one}а`}
              defaultValue={pin.name}
              key={pin.name}
              readOnly={readOnly}
              onBlur={(event) => {
                const renamed = event.target.value.trim();

                if (renamed && renamed !== pin.name) {
                  onChange(renamePin(definition, catalog, which, pin.name, renamed));
                }
              }}
              className={input}
            />
            <select
              aria-label={`Тип ${pin.name}`}
              value={pin.type}
              disabled={readOnly}
              onChange={(event) =>
                replace(
                  pins.map((item, at) =>
                    at === index ? { ...item, type: event.target.value as PinType } : item,
                  ),
                )
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
                aria-label={`Удалить ${one} ${pin.name}`}
                onClick={() => replace(pins.filter((_, at) => at !== index))}
                className="px-1 text-faint hover:text-err"
              >
                ×
              </button>
            )}
          </div>
        ))}
      </div>
    );
  };

  return (
    <Card label="Составной узел" title="Составной узел" onClose={onClose}>
      <Field label="Название">
        <input
          aria-label="Название"
          value={name}
          readOnly={readOnly}
          onChange={(event) => onName(event.target.value)}
          className="h-8 w-full rounded-md border border-line bg-panel-2 px-2.5 text-sm"
        />
      </Field>
      {side('inputs', 'Входы', 'вход')}
      {side('outputs', 'Выходы', 'выход')}
    </Card>
  );
}
