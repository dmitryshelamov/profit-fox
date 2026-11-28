import { useQueryClient } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';

import type { RequestError } from '@/api/client';
import type { Written } from '@/api/signals';
import {
  type DraftOwner,
  moveDraft,
  readDraft,
  removeDraft,
} from '@/modules/graph-editor/drafts/drafts';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { Menu, type MenuItem } from '@/shared/ui/Menu';
import { PageTitle } from '@/shared/ui/Page';
import { Table, Td, Th, Tr } from '@/shared/ui/Table';
import { TextField } from '@/shared/ui/TextField';
import { useStoredState } from '@/shared/ui/useStoredState';
import { VersionBadge } from '@/shared/ui/VersionBadge';

/** A line of a list of saved documents: what the list shows of one. */
export interface DocumentLine {
  readonly name: string;
  readonly description?: string | null;
  readonly version: number | string;
  readonly error?: string | null;
}

/** A column of one section: «Где используется» of signals, «Сигналы» and «Выход» of strategies. */
export interface DocumentColumn<Line extends DocumentLine> {
  readonly title: string;
  readonly className?: string;
  readonly cell: (line: Line) => ReactNode;
}

/**
 * The differences of the lists of signals and of strategies: their words, their keys, their requests and the columns
 * of their section. Rename, copy and delete are one table (spec signal-constructor «Создание, переименование,
 * копирование и удаление», spec bench-ui «Меню строки»).
 */
export interface ListType<Line extends DocumentLine = DocumentLine> {
  /** «Сигнал», «Стратегия»: the label of a line. */
  readonly label: string;
  /** «сигнал», «стратегию»: what the dialogs rename, copy and delete. */
  readonly object: string;
  readonly queryKey: 'signal' | 'strategy';
  readonly listKey: 'signals' | 'strategies';
  readonly draftKey: (owner: DraftOwner) => string;
  /** The link to the editor of a document, drawn with the classes of the table. */
  readonly link: (name: string, className: string) => ReactNode;
  readonly columns: readonly DocumentColumn<Line>[];
  /** What stands before the menu of a row, as «Создать стратегию» of a signal. */
  readonly actions?: (line: Line) => ReactNode;
  /** The items of the menu of a row before rename, copy and delete, as «Прогнать» of a strategy. */
  readonly menu?: (line: Line) => MenuItem[];
  readonly rename: (name: string, to: string) => Promise<Written<unknown>>;
  /** Saves a copy under a new name: its description, graph and layout read from the source. */
  readonly copy: (name: string, to: string) => Promise<Written<unknown>>;
  /** Deletes, or gives the errors of the refusal. */
  readonly remove: (name: string) => Promise<readonly RequestError[]>;
  readonly deleteQuestion: (name: string) => string;
}

type Asked =
  | { readonly kind: 'rename' | 'copy'; readonly line: string; readonly name: string }
  | { readonly kind: 'delete'; readonly line: string }
  | null;

/**
 * The table of saved documents with its title and search (change redesign-bench-ui, design.md, decision 6): a row per
 * document with its name, description, version and the columns of its section; rename, copy and delete are in the menu
 * of the row, the name is asked and the delete confirmed in a dialog, and a refusal of the API stays in that dialog.
 * The search is the state of the page: a row stays when its name holds the text, whatever the case (spec bench-ui
 * «Поиск в списке»).
 */
export function DocumentTable<Line extends DocumentLine>({
  type,
  title,
  hint,
  label,
  lines,
  pending,
  problem,
  action,
  empty,
  tail,
}: {
  readonly type: ListType<Line>;
  readonly title: string;
  readonly hint: string;
  /** The accessible name of the section with the table. */
  readonly label: string;
  readonly lines: readonly Line[] | undefined;
  readonly pending: boolean;
  readonly problem: string | null;
  /** The main action of the page, as «Новый сигнал». */
  readonly action: ReactNode;
  /** What stands in place of the table when nothing is saved. */
  readonly empty: ReactNode;
  /** Rows after the documents that are not documents, as the built-in label of the signals. */
  readonly tail?: ReactNode;
}) {
  const queries = useQueryClient();
  const [search, setSearch] = useStoredState(`profit-fox.search.${type.listKey}`, '', (stored) =>
    typeof stored === 'string' ? stored : undefined,
  );
  const [asked, setAsked] = useState<Asked>(null);
  const [errors, setErrors] = useState<readonly RequestError[]>([]);
  const [busy, setBusy] = useState(false);

  const wanted = search.trim().toLowerCase();
  const found = lines?.filter((line) => line.name.toLowerCase().includes(wanted));

  const ask = (next: Asked) => {
    setErrors([]);
    setAsked(next);
  };
  const refresh = () => queries.invalidateQueries({ queryKey: [type.listKey] });
  const failed = (thrown: unknown): RequestError[] => [
    { code: 'Failed', reason: thrown instanceof Error ? thrown.message : String(thrown) },
  ];

  const submitName = async () => {
    if (asked === null || asked.kind === 'delete') {
      return;
    }

    setErrors([]);
    setBusy(true);
    try {
      const outcome =
        asked.kind === 'rename'
          ? await type.rename(asked.line, asked.name)
          : await type.copy(asked.line, asked.name);
      if (!outcome.ok) {
        setErrors(outcome.errors);
        return;
      }

      if (asked.kind === 'rename') {
        moveDraft(type.draftKey({ name: asked.line }), type.draftKey({ name: asked.name }));
        queries.removeQueries({ queryKey: [type.queryKey, asked.line] });
      }

      setAsked(null);
      await refresh();
    } catch (thrown) {
      setErrors(failed(thrown));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (asked?.kind !== 'delete') {
      return;
    }

    setErrors([]);
    setBusy(true);
    try {
      const refused = await type.remove(asked.line);
      if (refused.length > 0) {
        setErrors(refused);
        return;
      }

      removeDraft(type.draftKey({ name: asked.line }));
      queries.removeQueries({ queryKey: [type.queryKey, asked.line] });
      setAsked(null);
      await refresh();
    } catch (thrown) {
      setErrors(failed(thrown));
    } finally {
      setBusy(false);
    }
  };

  const itemsOf = (line: Line): MenuItem[] => [
    ...(line.error
      ? []
      : [
          ...(type.menu?.(line) ?? []),
          {
            label: 'Переименовать',
            onSelect: () => ask({ kind: 'rename', line: line.name, name: line.name }),
          },
          {
            label: 'Копировать',
            onSelect: () => ask({ kind: 'copy', line: line.name, name: `${line.name}-copy` }),
          },
        ]),
    { label: 'Удалить', danger: true, onSelect: () => ask({ kind: 'delete', line: line.name }) },
  ];

  return (
    <section aria-label={label} className="flex flex-col gap-5">
      <PageTitle title={title} hint={hint}>
        <TextField
          aria-label="Найти по имени"
          type="search"
          sign="⌕"
          placeholder="Найти по имени"
          className="w-[260px]"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        {action}
      </PageTitle>

      {pending && <p className="text-muted">Загрузка…</p>}
      {problem !== null && <p className="text-err">{problem}</p>}
      {lines?.length === 0 && empty}
      {lines !== undefined && lines.length > 0 && found?.length === 0 && (
        <p role="status" className="text-muted">
          По запросу <code className="font-mono text-text">{search.trim()}</code> ничего не найдено
        </p>
      )}
      {found !== undefined && found.length > 0 && (
        <Table>
          <thead>
            <tr>
              <Th className="w-[240px]">Имя</Th>
              <Th className="w-[310px]">Описание</Th>
              <Th className="w-[70px]">Версия</Th>
              {type.columns.map((column) => (
                <Th key={column.title} className={column.className}>
                  {column.title}
                </Th>
              ))}
              <Th className="w-px">
                <span className="sr-only">Действия</span>
              </Th>
            </tr>
          </thead>
          <tbody>
            {found.map((line) => (
              <Tr key={line.name} aria-label={`${type.label} ${line.name}`} className="h-14">
                <Td>
                  <span className="flex items-center gap-3">
                    {type.link(
                      line.name,
                      `font-mono font-medium hover:text-accent ${line.error ? 'text-muted' : ''}`,
                    )}
                    {readDraft(type.draftKey({ name: line.name })) !== null && (
                      <Badge tone="warn">черновик</Badge>
                    )}
                  </span>
                </Td>
                {line.error ? (
                  <Td colSpan={2 + type.columns.length}>
                    <span className="flex items-center gap-4">
                      <Badge tone="err" dot>
                        не читается
                      </Badge>
                      <span className="text-xs text-err">Файл не читается: {line.error}</span>
                      {Number(line.version) > 0 && <VersionBadge version={line.version} />}
                    </span>
                  </Td>
                ) : (
                  <>
                    <Td className="text-muted">{line.description}</Td>
                    <Td>
                      <VersionBadge version={line.version} />
                    </Td>
                    {type.columns.map((column) => (
                      <Td key={column.title}>{column.cell(line)}</Td>
                    ))}
                  </>
                )}
                <Td>
                  <span className="flex items-center justify-end gap-2">
                    {!line.error && type.actions?.(line)}
                    <Menu label={`Действия ${line.name}`} items={itemsOf(line)} />
                  </span>
                </Td>
              </Tr>
            ))}
            {tail}
          </tbody>
        </Table>
      )}

      {asked !== null && asked.kind !== 'delete' && (
        <Dialog
          label={`${asked.kind === 'rename' ? 'Переименовать' : 'Копировать'} ${type.object} ${asked.line}`}
          title={`${asked.kind === 'rename' ? 'Переименовать' : 'Копировать'} ${type.object}`}
          onClose={() => setAsked(null)}
        >
          <form
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault();
              void submitName();
            }}
          >
            <label className="flex flex-col gap-1.5 text-xs text-muted">
              {asked.kind === 'rename' ? 'Новое имя' : 'Имя копии'}
              <TextField
                aria-label={asked.kind === 'rename' ? 'Новое имя' : 'Имя копии'}
                mono
                autoFocus
                value={asked.name}
                onChange={(event) => setAsked({ ...asked, name: event.target.value.trim() })}
              />
              <span className="text-faint">Строчные латинские буквы и цифры через дефис.</span>
            </label>
            <Refusal errors={errors} />
            <span className="flex justify-end gap-2">
              <Button onClick={() => setAsked(null)}>Отмена</Button>
              <Button type="submit" variant="primary" disabled={asked.name === '' || busy}>
                {asked.kind === 'rename' ? 'Переименовать' : 'Скопировать'}
              </Button>
            </span>
          </form>
        </Dialog>
      )}
      {asked?.kind === 'delete' && (
        <Dialog
          label={`Удалить ${type.object} ${asked.line}`}
          title={`Удалить ${type.object}`}
          onClose={() => setAsked(null)}
        >
          <p>{type.deleteQuestion(asked.line)}</p>
          <Refusal errors={errors} />
          <span className="flex justify-end gap-2">
            <Button onClick={() => setAsked(null)}>Отмена</Button>
            <Button variant="danger" className="border border-err" disabled={busy} onClick={remove}>
              Удалить
            </Button>
          </span>
        </Dialog>
      )}
    </section>
  );
}

/** Why the API refused, inside the dialog that asked. */
function Refusal({ errors }: { readonly errors: readonly RequestError[] }) {
  return errors.length === 0 ? null : (
    <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
      {errors.map((error) => error.reason).join('; ')}
    </p>
  );
}
