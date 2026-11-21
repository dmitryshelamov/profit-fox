import { useQueryClient } from '@tanstack/react-query';
import { Link, useBlocker, useNavigate } from '@tanstack/react-router';
import { type ReactNode, useEffect, useRef, useState } from 'react';

import { api, type RequestError, unwrap } from '@/api/client';
import type { Written } from '@/api/signals';
import type { DocumentRules } from '@/modules/graph-editor/document';
import { readDraft, removeDraft, writeDraft } from '@/modules/graph-editor/drafts/drafts';
import { useGraphCheck } from '@/modules/graph-editor/editing/useGraphCheck';
import { useSignalNodes } from '@/modules/graph-editor/editing/useSignalNodes';
import { differences, sameDocument } from '@/modules/graph-editor/model/compare';
import { layoutOfNodes } from '@/modules/graph-editor/model/operations';
import {
  referenceOf,
  signalInfo,
  type SignalUse,
  updateSignalVersion,
} from '@/modules/graph-editor/model/signals';
import type { Graph, GraphDocument, Layout, NodeKind } from '@/modules/graph-editor/model/types';
import { buttonClass, fieldClass } from '@/modules/graph-editor/ui/Card';
import { DraftQuestion } from '@/modules/graph-editor/ui/DraftQuestion';
import { GraphEditor } from '@/modules/graph-editor/ui/GraphEditor';

/** The delay after an edit before the draft is written to the browser. */
export const draftDelayMs = 300;

/** What a saved document holds apart from its name. */
export interface Content {
  readonly description: string;
  readonly document: GraphDocument;
}

/** The body of a save: what the API keeps of a document. */
export interface SaveBody {
  readonly description: string | null;
  readonly graph: never;
  readonly layout: never;
}

/**
 * The differences of the editors of a signal and of a strategy: their rules, their words, their routes and the
 * requests that save them. Everything else — the canvas, the check, the draft, the question about it, the name of the
 * first save — is one editor.
 */
export interface DocumentType {
  readonly rules: DocumentRules;
  readonly section: '/signals' | '/strategies';
  readonly sectionTitle: string;
  readonly newTitle: string;
  readonly nameLabel: string;
  readonly namePlaceholder: string;
  /** The keys of the queries of one document and of the list. */
  readonly queryKey: 'signal' | 'strategy';
  readonly listKey: 'signals' | 'strategies';
  readonly create: (body: SaveBody & { readonly name: string }) => Promise<Written<unknown>>;
  readonly replace: (name: string, body: SaveBody) => Promise<Written<unknown>>;
  /** Opens the saved document after its first save. */
  readonly open: (navigate: ReturnType<typeof useNavigate>, name: string) => Promise<void>;
}

/**
 * The editor of one document: the canvas, the check, the draft in the browser, saving, and the bottom panel. A saved
 * document opened with a draft that differs from it asks to restore or discard it.
 */
export function DocumentEditor({
  type,
  saved,
  draftId,
  startVersion,
  catalog,
  start,
  startSignals = [],
  actions,
  bottom,
}: {
  readonly type: DocumentType;
  readonly saved: { readonly name: string } | null;
  readonly startVersion?: number;
  readonly draftId?: string;
  readonly catalog: readonly NodeKind[];
  readonly start: Content;
  /** What the signal nodes of a saved strategy point at, as its read gave it. */
  readonly startSignals?: readonly SignalUse[];
  readonly actions?: ReactNode;
  readonly bottom?: (state: {
    readonly graph: Graph;
    readonly canRun: boolean;
    readonly rememberAs: string;
  }) => ReactNode;
}) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const rules = type.rules;
  const key = saved ? rules.draftKey(saved) : rules.draftKey({ newId: draftId! });
  const [file, setFile] = useState<Content | null>(saved ? start : null);
  const [content, setContent] = useState<Content>(start);
  const [urgent, setUrgent] = useState<Graph | null>(null);
  const [version, setVersion] = useState(startVersion);
  const [question, setQuestion] = useState(() => {
    if (!saved) {
      return null;
    }

    const draft = readDraft(key);
    const drafted = draft && {
      description: draft.description,
      document: { graph: draft.graph, layout: draft.layout },
    };
    return drafted && !sameContent(drafted, start)
      ? { draft: drafted, savedAt: draft.savedAt }
      : null;
  });
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState('');
  const [saveErrors, setSaveErrors] = useState<readonly RequestError[]>([]);
  const [saving, setSaving] = useState(false);
  const check = useGraphCheck(content.document.graph, rules.checkPath, urgent);
  const signals = useSignalNodes(rules.kind === 'strategy', catalog, startSignals, check.signals);

  const dirty = file === null ? true : !sameContent(content, file);
  const canSave = dirty && check.checked && check.errors.length === 0 && !saving;

  const change = (document: GraphDocument, isUrgent?: boolean) => {
    if (isUrgent) {
      setUrgent(document.graph);
    }

    setContent((current) => ({ ...current, document }));
  };

  // The update of a signal node reads the latest version of its signal: its parameters say which inputs stay.
  const updateSignal = async (node: string) => {
    const found = content.document.graph.nodes.find((candidate) => candidate.id === node);
    const info = found && signalInfo(found, signals.catalog);
    const reference = found && referenceOf(found);
    if (!found || !info?.name || !reference) {
      return;
    }

    const signal = unwrap(
      await api.GET('/api/signals/{name}', { params: { path: { name: info.name } } }),
    );
    const graph = signal.graph as unknown as Graph;
    const latest = Number(signal.version);
    signals.learn(signal.id, latest, {
      name: signal.name,
      latestVersion: latest,
      parameters: graph.parameters.map((parameter) => ({ ...parameter })),
    });
    change(
      updateSignalVersion(
        content.document,
        node,
        latest,
        graph.parameters.map((parameter) => parameter.name),
      ),
      true,
    );
  };

  useEffect(() => {
    if (question) {
      return;
    }

    const timer = setTimeout(() => {
      if (file !== null && sameContent(content, file)) {
        removeDraft(key);
      } else {
        writeDraft(key, { description: content.description, ...content.document });
      }
    }, draftDelayMs);
    return () => clearTimeout(timer);
  }, [content, file, key, question]);

  // The way from a new document to its saved page right after the first save is not leaving it.
  const savedAway = useRef(false);
  useBlocker({
    shouldBlockFn: () =>
      dirty &&
      !savedAway.current &&
      !window.confirm('Есть несохранённые правки. Уйти? Черновик останется в браузере.'),
    enableBeforeUnload: () => dirty && !savedAway.current,
  });

  const save = async (as?: string) => {
    setSaving(true);
    setSaveErrors([]);
    const body: SaveBody = {
      description: content.description.trim() === '' ? null : content.description.trim(),
      graph: content.document.graph as never,
      layout: layoutOfNodes(content.document) as never,
    };
    try {
      const outcome = saved
        ? await type.replace(saved.name, body)
        : await type.create({ ...body, name: as ?? '' });
      if (!outcome.ok) {
        setSaveErrors(outcome.errors);
        return;
      }

      removeDraft(key);
      await queries.invalidateQueries({ queryKey: [type.listKey] });
      if (saved) {
        const next = (outcome.value as { version?: number | string }).version;
        const nextVersion = next === undefined ? version : Number(next);
        queries.setQueryData([type.queryKey, saved.name], (current: object | undefined) => ({
          ...current,
          content,
          version: nextVersion,
        }));
        setVersion(nextVersion);
        setFile(content);
      } else {
        const created = outcome.value as { name: string };
        queries.removeQueries({ queryKey: [type.queryKey, created.name] });
        savedAway.current = true;
        await type.open(navigate, created.name);
      }
    } catch (problem) {
      setSaveErrors([
        {
          code: 'SaveFailed',
          reason: String(problem instanceof Error ? problem.message : problem),
        },
      ]);
    } finally {
      setSaving(false);
    }
  };

  const nameError = saveErrors.find((error) => error.field === 'name');
  const otherErrors = saveErrors.filter((error) => error.field !== 'name');

  return (
    <>
      <GraphEditor
        title={
          <>
            <Link to={type.section} className="text-zinc-400 hover:text-zinc-100">
              ← {type.sectionTitle}
            </Link>
            <span className="font-mono font-semibold">{saved?.name ?? type.newTitle}</span>
            {saved && version !== undefined && (
              <span className="text-xs text-zinc-400">версия {version}</span>
            )}
            <input
              aria-label="Описание"
              className={`${fieldClass} w-64 font-sans`}
              placeholder="описание"
              value={content.description}
              onChange={(event) => setContent({ ...content, description: event.target.value })}
            />
            <span role="status" aria-label="Состояние" className="text-xs text-zinc-400">
              {!check.checked && !check.failed
                ? 'проверяю…'
                : check.failed
                  ? 'проверка не удалась'
                  : dirty
                    ? 'не сохранено'
                    : 'сохранено'}
            </span>
          </>
        }
        document={content.document}
        catalog={signals.catalog}
        rules={rules}
        errors={check.errors}
        checkFailed={check.failed}
        readOnly={false}
        signals={
          rules.kind === 'strategy'
            ? { palette: signals.palette, onUpdate: (node) => void updateSignal(node) }
            : undefined
        }
        onChange={change}
        actions={
          <>
            {file !== null && (
              <button
                type="button"
                className={buttonClass}
                disabled={!dirty}
                onClick={() => {
                  setContent(file);
                  removeDraft(key);
                }}
              >
                Отбросить
              </button>
            )}
            <button
              type="button"
              className={`${buttonClass} border-amber-500`}
              disabled={!canSave}
              onClick={() => (saved ? void save() : setNaming(true))}
            >
              {saving ? 'Сохраняю…' : 'Сохранить'}
            </button>
            {actions}
            {otherErrors.length > 0 && (
              <span className="text-xs text-red-300">
                {otherErrors.map((error) => error.reason).join('; ')}
              </span>
            )}
          </>
        }
        bottom={bottom?.({
          graph: content.document.graph,
          canRun: check.checked && check.errors.length === 0,
          rememberAs: saved?.name ?? `new:${draftId}`,
        })}
      />
      {question && (
        <DraftQuestion
          savedAt={question.savedAt}
          {...differences(start.document, question.draft.document)}
          onRestore={() => {
            setContent(question.draft);
            setQuestion(null);
          }}
          onDiscard={() => {
            removeDraft(key);
            setQuestion(null);
          }}
        />
      )}
      {naming && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-black/60">
          <form
            role="dialog"
            aria-label={type.nameLabel}
            className="flex w-96 flex-col gap-3 rounded-lg border border-zinc-700 bg-zinc-900 p-4 text-sm"
            onSubmit={async (event) => {
              event.preventDefault();
              await save(name);
            }}
          >
            <label className="flex flex-col gap-1">
              {type.nameLabel}
              <input
                aria-label="Имя"
                className={fieldClass}
                placeholder={type.namePlaceholder}
                value={name}
                autoFocus
                onChange={(event) => setName(event.target.value.trim())}
              />
              <span className="text-xs text-zinc-500">
                Строчные латинские буквы и цифры через дефис.
              </span>
            </label>
            {nameError && <p className="text-xs text-red-300">{nameError.reason}</p>}
            <span className="flex justify-end gap-2">
              <button type="button" className={buttonClass} onClick={() => setNaming(false)}>
                Отмена
              </button>
              <button
                type="submit"
                className={`${buttonClass} border-amber-500`}
                disabled={name === '' || saving}
              >
                Сохранить
              </button>
            </span>
          </form>
        </div>
      )}
    </>
  );
}

/** The content of a new document: its draft, or the start of its rules. */
export function newContent(rules: DocumentRules, draftId: string): Content {
  const draft = readDraft(rules.draftKey({ newId: draftId }));
  return draft
    ? { description: draft.description, document: { graph: draft.graph, layout: draft.layout } }
    : { description: '', document: { graph: rules.start(), layout: {} } };
}

function sameContent(a: Content, b: Content): boolean {
  return a.description.trim() === b.description.trim() && sameDocument(a.document, b.document);
}

export function toLayout(
  layout: Readonly<Record<string, { x: number | string; y: number | string }>>,
): Layout {
  return Object.fromEntries(
    Object.entries(layout).map(([id, place]) => [id, { x: Number(place.x), y: Number(place.y) }]),
  );
}

export function Problem({
  text,
  quiet = false,
  section,
}: {
  readonly text: string;
  readonly quiet?: boolean;
  readonly section: { readonly to: '/signals' | '/strategies'; readonly title: string };
}) {
  return (
    <main className="flex min-h-screen flex-col items-start gap-3 bg-zinc-950 p-6 text-sm text-zinc-100">
      <Link to={section.to} className="text-zinc-400 hover:text-zinc-100">
        ← {section.title}
      </Link>
      <p className={quiet ? 'text-zinc-400' : 'text-red-300'}>{text}</p>
    </main>
  );
}
