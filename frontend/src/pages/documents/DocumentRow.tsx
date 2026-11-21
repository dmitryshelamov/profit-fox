import { useQueryClient } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';

import type { RequestError } from '@/api/client';
import type { Written } from '@/api/signals';
import { type DraftOwner, moveDraft, removeDraft } from '@/modules/graph-editor/drafts/drafts';
import { buttonClass, fieldClass } from '@/modules/graph-editor/ui/Card';

/** A line of a list of saved documents: what the list shows of one. */
export interface DocumentLine {
  readonly name: string;
  readonly description?: string | null;
  readonly version: number | string;
  readonly error?: string | null;
}

/**
 * The differences of the lists of signals and of strategies: their words, their keys and their requests. Rename,
 * copy and delete are one row (spec signal-constructor «Создание, переименование, копирование и удаление»).
 */
export interface ListType {
  /** «Сигнал», «Стратегия»: the label of a line. */
  readonly label: string;
  readonly queryKey: 'signal' | 'strategy';
  readonly listKey: 'signals' | 'strategies';
  readonly draftKey: (owner: DraftOwner) => string;
  readonly link: (name: string) => ReactNode;
  readonly rename: (name: string, to: string) => Promise<Written<unknown>>;
  /** Saves a copy under a new name: its description, graph and layout read from the source. */
  readonly copy: (name: string, to: string) => Promise<Written<unknown>>;
  /** Deletes, or gives the errors of the refusal. */
  readonly remove: (name: string) => Promise<readonly RequestError[]>;
  readonly deleteQuestion: (name: string) => string;
}

type Mode = { readonly kind: 'rename' | 'copy'; readonly name: string } | null;

export function DocumentRow({
  type,
  line,
  actions,
}: {
  readonly type: ListType;
  readonly line: DocumentLine;
  readonly actions?: ReactNode;
}) {
  const queries = useQueryClient();
  const [mode, setMode] = useState<Mode>(null);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);

  const refresh = () => queries.invalidateQueries({ queryKey: [type.listKey] });

  const submit = async () => {
    if (mode === null) {
      return;
    }

    setErrors([]);
    try {
      const outcome =
        mode.kind === 'rename'
          ? await type.rename(line.name, mode.name)
          : await type.copy(line.name, mode.name);
      if (!outcome.ok) {
        setErrors(outcome.errors);
        return;
      }

      if (mode.kind === 'rename') {
        moveDraft(type.draftKey({ name: line.name }), type.draftKey({ name: mode.name }));
        queries.removeQueries({ queryKey: [type.queryKey, line.name] });
      }

      setMode(null);
      await refresh();
    } catch (problem) {
      setErrors([
        { code: 'Failed', reason: problem instanceof Error ? problem.message : String(problem) },
      ]);
    }
  };

  const remove = async () => {
    if (!window.confirm(type.deleteQuestion(line.name))) {
      return;
    }

    setErrors([]);
    const refused = await type.remove(line.name);
    if (refused.length > 0) {
      setErrors(refused);
      return;
    }

    removeDraft(type.draftKey({ name: line.name }));
    queries.removeQueries({ queryKey: [type.queryKey, line.name] });
    await refresh();
  };

  return (
    <li aria-label={`${type.label} ${line.name}`} className="flex flex-col gap-2 px-3 py-2">
      <div className="flex items-center gap-3">
        {type.link(line.name)}
        <span className="text-zinc-500">версия {String(line.version)}</span>
        {line.description && <span className="text-zinc-400">{line.description}</span>}
        <span className="ml-auto flex gap-2">
          {actions}
          <button
            type="button"
            className={buttonClass}
            onClick={() => setMode({ kind: 'rename', name: line.name })}
          >
            Переименовать
          </button>
          {!line.error && (
            <button
              type="button"
              className={buttonClass}
              onClick={() => setMode({ kind: 'copy', name: `${line.name}-copy` })}
            >
              Копировать
            </button>
          )}
          <button type="button" className={buttonClass} onClick={remove}>
            Удалить
          </button>
        </span>
      </div>
      {line.error && <p className="text-xs text-red-300">Файл не читается: {line.error}</p>}
      {mode === null && errors.length > 0 && (
        <p role="alert" className="text-xs text-red-300">
          {errors.map((error) => error.reason).join('; ')}
        </p>
      )}
      {mode && (
        <form
          className="flex items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <input
            aria-label={mode.kind === 'rename' ? 'Новое имя' : 'Имя копии'}
            className={`${fieldClass} w-64`}
            value={mode.name}
            autoFocus
            onChange={(event) => setMode({ ...mode, name: event.target.value.trim() })}
          />
          <button type="submit" className={buttonClass} disabled={mode.name === ''}>
            {mode.kind === 'rename' ? 'Переименовать' : 'Скопировать'}
          </button>
          <button type="button" className={buttonClass} onClick={() => setMode(null)}>
            Отмена
          </button>
          {errors.length > 0 && (
            <span className="text-xs text-red-300">
              {errors.map((error) => error.reason).join('; ')}
            </span>
          )}
        </form>
      )}
    </li>
  );
}
