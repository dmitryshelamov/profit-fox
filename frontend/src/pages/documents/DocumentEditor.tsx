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
import { DraftQuestion } from '@/modules/graph-editor/ui/DraftQuestion';
import { BarDivider } from '@/modules/graph-editor/ui/FloatingBar';
import { GraphEditor } from '@/modules/graph-editor/ui/GraphEditor';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { TextField } from '@/shared/ui/TextField';
import { VersionBadge } from '@/shared/ui/VersionBadge';

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

/** Where the document stands, for the actions of its editor. */
export interface EditorState {
  /** The document differs from what the store keeps, or has no first save yet. */
  readonly dirty: boolean;
  /** The last check found errors. */
  readonly hasErrors: boolean;
  /** The version the store keeps; none before the first save. */
  readonly version?: number;
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
 * document opened with a draft that differs from it asks to restore or discard it. A saved document read `raised` is
 * not what the store keeps: it opens as not saved, and saving it as it is makes its next version.
 */
export function DocumentEditor({
  type,
  saved,
  draftId,
  startVersion,
  raised = false,
  catalog,
  start,
  startSignals = [],
  plaque,
  actions,
  bottom,
}: {
  readonly type: DocumentType;
  readonly saved: { readonly name: string } | null;
  readonly startVersion?: number;
  /** The store keeps the graph in a former form; `start` is that graph raised on reading. */
  readonly raised?: boolean;
  readonly draftId?: string;
  readonly catalog: readonly NodeKind[];
  readonly start: Content;
  /** What the signal nodes of a saved strategy point at, as its read gave it. */
  readonly startSignals?: readonly SignalUse[];
  /** What the plaque of the document shows after its name and version, as the kind of a strategy. */
  readonly plaque?: ReactNode;
  /** What stands after «Сохранить»; it may depend on where the document stands. */
  readonly actions?: ReactNode | ((state: EditorState) => ReactNode);
  readonly bottom?: (state: {
    readonly graph: Graph;
    readonly canRun: boolean;
    /** The check found errors: the preview says why it cannot run. */
    readonly hasErrors: boolean;
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
  const [stored, setStored] = useState(!raised);
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
  const check = useGraphCheck(content.document.graph, rules.checkPath, urgent, rules.strategyKind);
  const signals = useSignalNodes(rules.kind === 'strategy', catalog, startSignals, check.signals);

  // Edited: the content differs from what was read. Dirty: it differs from what the store keeps, which a document
  // read raised does before any edit. Leaving such a document loses nothing, so only an edit asks before leaving.
  const edited = file === null ? true : !sameContent(content, file);
  const dirty = edited || !stored;
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
        writeDraft(key, {
          description: content.description,
          ...content.document,
          ...(rules.strategyKind === undefined ? {} : { kind: rules.strategyKind }),
        });
      }
    }, draftDelayMs);
    return () => clearTimeout(timer);
  }, [content, file, key, question, rules.strategyKind]);

  // The way from a new document to its saved page right after the first save is not leaving it.
  const savedAway = useRef(false);
  useBlocker({
    shouldBlockFn: () =>
      edited &&
      !savedAway.current &&
      !window.confirm('Есть несохранённые правки. Уйти? Черновик останется в браузере.'),
    enableBeforeUnload: () => edited && !savedAway.current,
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
          // What was read raised is stored in the new form now.
          ...(current && 'raised' in current ? { raised: false } : {}),
        }));
        setVersion(nextVersion);
        setFile(content);
        setStored(true);
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
  // The state of the document: its words are read by the name «Состояние», its tone is the color.
  const state =
    !check.checked && !check.failed
      ? { key: 'checking', words: 'проверяю…', tone: 'text-warn' }
      : check.failed
        ? { key: 'failed', words: 'проверка не удалась', tone: 'text-err' }
        : dirty
          ? { key: 'dirty', words: 'не сохранено', tone: 'text-warn' }
          : { key: 'saved', words: 'сохранено', tone: 'text-muted' };

  return (
    <>
      <GraphEditor
        title={
          <>
            <SectionLink to={type.section} title={type.sectionTitle} />
            <BarDivider />
            <span className="shrink-0 font-mono font-semibold">{saved?.name ?? type.newTitle}</span>
            {saved && version !== undefined && <VersionBadge version={version} />}
            {plaque}
            <BarDivider />
            <label className="flex min-w-0 items-center gap-2">
              <input
                aria-label="Описание"
                className="w-64 min-w-0 bg-transparent text-muted outline-none placeholder:text-faint focus:text-text"
                placeholder="описание"
                value={content.description}
                onChange={(event) => setContent({ ...content, description: event.target.value })}
              />
              <span aria-hidden="true" className="text-faint">
                ✎
              </span>
            </label>
            <BarDivider />
            <span
              role="status"
              aria-label="Состояние"
              data-state={state.key}
              className={`flex shrink-0 items-center gap-1.5 text-xs ${state.tone}`}
            >
              <span aria-hidden="true" className="size-1.5 rounded-full bg-current" />
              {state.words}
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
              <Button
                variant="plain"
                disabled={!edited}
                onClick={() => {
                  setContent(file);
                  removeDraft(key);
                }}
              >
                Отбросить
              </Button>
            )}
            <Button
              variant="primary"
              disabled={!canSave}
              onClick={() => (saved ? void save() : setNaming(true))}
            >
              {saving ? 'Сохраняю…' : 'Сохранить'}
            </Button>
            {typeof actions === 'function'
              ? actions({
                  dirty,
                  hasErrors: check.checked && check.errors.length > 0,
                  version: saved ? version : undefined,
                })
              : actions}
            {otherErrors.length > 0 && (
              <span role="alert" className="max-w-64 truncate px-1.5 text-xs text-err">
                {otherErrors.map((error) => error.reason).join('; ')}
              </span>
            )}
          </>
        }
        bottom={bottom?.({
          graph: content.document.graph,
          canRun: check.checked && check.errors.length === 0,
          hasErrors: check.checked && check.errors.length > 0,
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
        <Dialog label={type.nameLabel} title={type.nameLabel} onClose={() => setNaming(false)}>
          <form
            className="flex flex-col gap-4"
            onSubmit={async (event) => {
              event.preventDefault();
              await save(name);
            }}
          >
            <label className="flex flex-col gap-1.5 text-xs text-muted">
              <TextField
                aria-label="Имя"
                mono
                placeholder={type.namePlaceholder}
                value={name}
                autoFocus
                onChange={(event) => setName(event.target.value.trim())}
              />
              <span className="text-faint">Строчные латинские буквы и цифры через дефис.</span>
            </label>
            {nameError && (
              <p role="alert" className="rounded-md bg-err-bg px-3 py-2 text-xs text-err">
                {nameError.reason}
              </p>
            )}
            <span className="flex justify-end gap-2">
              <Button onClick={() => setNaming(false)}>Отмена</Button>
              <Button type="submit" variant="primary" disabled={name === '' || saving}>
                Сохранить
              </Button>
            </span>
          </form>
        </Dialog>
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
    <main className="flex min-h-screen flex-col items-start gap-3 bg-canvas p-6">
      <SectionLink to={section.to} title={section.title} />
      <p className={quiet ? 'text-muted' : 'text-err'}>{text}</p>
    </main>
  );
}

/** The way from an editor back to its section: the editors have no header. */
export function SectionLink({
  to,
  title,
}: {
  readonly to: '/signals' | '/strategies';
  readonly title: string;
}) {
  return (
    <Link to={to} className="flex shrink-0 items-center gap-1.5 text-muted hover:text-text">
      <span aria-hidden="true">←</span>
      {title}
    </Link>
  );
}
