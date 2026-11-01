import { useEffect, useMemo, useState } from 'react';

import { type AffectedBot, ApiError, type Usage } from '@/shared/api/client';

import { drafts } from '../drafts/drafts';
import { createDraftWriter } from '../drafts/writer';
import { compareDrafts, type Difference, sameEdit } from '../model/compare';
import { type Issue, issuesOf } from '../model/issues';
import type { AnyDef } from '../model/types';
import { createChecker } from './checker';

export interface Edited<T extends AnyDef> {
  readonly name: string;
  readonly definition: T;
}

/** What saving would reject: the answer of the check endpoints. */
export interface Report {
  readonly errors: Readonly<Record<string, readonly string[]>>;
  readonly usages: readonly Usage[];
  readonly bots: readonly AffectedBot[];
}

/** Why the server rejected a save. A conflict without usages or bots means the library changed during the save. */
export interface Rejection {
  readonly message: string;
  readonly usages: readonly Usage[];
  readonly bots: readonly AffectedBot[];
  readonly conflict?: boolean;
}

/** A draft left in the browser that differs from what is saved: the editor offers to restore or drop it. */
export interface DraftOffer {
  readonly savedAt: string;
  readonly difference: Difference;
}

const clean: Report = { errors: {}, usages: [], bots: [] };

/**
 * The state of an editor: the edited name and definition, the server's check of them and their draft in the browser.
 * A check runs after edits pause; while it runs or finds errors, the edit cannot be saved.
 */
export function useGraphEditing<T extends AnyDef>({
  saved,
  initial,
  draftKey,
  validate,
}: {
  saved: Edited<T>;
  /** Where editing starts instead of what is saved, such as the draft of a strategy the server does not have yet. */
  initial?: Edited<T>;
  draftKey: string;
  validate: (edited: Edited<T>, signal: AbortSignal) => Promise<Report>;
}) {
  const [baseline, setBaseline] = useState(saved);
  const [edited, setEdited] = useState(initial ?? saved);
  const [saving, setSaving] = useState(false);
  const [rejection, setRejection] = useState<Rejection>();
  const [report, setReport] = useState(clean);
  const [checking, setChecking] = useState(false);
  const [offer, setOffer] = useState(() => (initial ? undefined : draftOffer(draftKey, saved)));
  const writer = useMemo(() => createDraftWriter(draftKey), [draftKey]);

  const checker = useMemo(
    () =>
      createChecker(
        async (next: Edited<T>, signal: AbortSignal) => {
          try {
            return await validate(next, signal);
          } catch (error) {
            // An empty name is rejected like a save, with errors keyed the same way.
            if (error instanceof ApiError && error.status === 400) {
              return { ...clean, errors: error.errors };
            }

            throw error;
          }
        },
        (result: Report) => {
          setReport(result);
          setChecking(false);
        },
      ),
    [validate],
  );

  useEffect(
    () => () => {
      checker.dispose();
      writer.cancel();
    },
    [checker, writer],
  );

  // An edit the editor starts with needs a check like any other.
  useEffect(() => {
    if (initial && !sameEdit(initial, saved)) {
      setChecking(true);
      checker.request(initial);
    }
    // Only the start of editing: later edits are checked as they come.
  }, []);

  const dirty = !sameEdit(baseline, edited);

  function change(next: Edited<T>) {
    setEdited(next);
    setChecking(true);
    checker.request(next);
    writer.write(next);
  }

  function reset(to: Edited<T>) {
    checker.dispose();
    writer.cancel();
    drafts.remove(draftKey);
    setEdited(to);
    setReport(clean);
    setChecking(false);
  }

  const issues: Issue[] = issuesOf(report.errors, edited.definition);
  const errorCount = issues.length + report.usages.length + report.bots.length;

  return {
    ...edited,
    dirty,
    checking,
    report,
    issues,
    errorCount,
    saving,
    rejection,
    offer,
    canSave: dirty && !checking && !saving && errorCount === 0,
    setName: (name: string) => change({ ...edited, name }),
    setDefinition: (definition: T) => change({ ...edited, definition }),
    /** Drops the edits and their draft and returns to what is saved. */
    discard: () => reset(baseline),
    restoreDraft() {
      const draft = drafts.read(draftKey);
      setOffer(undefined);

      if (draft) {
        change({ name: draft.name, definition: draft.definition as T });
      }
    },
    dropDraft() {
      setOffer(undefined);
      drafts.remove(draftKey);
    },
    dismissRejection: () => setRejection(undefined),
    /** Saves the edit; a rejected save keeps it and its draft and says why. */
    async save(persist: (edited: Edited<T>) => Promise<unknown>) {
      setSaving(true);
      setRejection(undefined);

      try {
        await persist(edited);
        setBaseline(edited);
        writer.cancel();
        drafts.remove(draftKey);
      } catch (error) {
        const failure =
          error instanceof ApiError ? error : new ApiError(0, (error as Error).message);

        if (failure.status === 400) {
          setReport({ ...clean, errors: failure.errors });
        } else {
          setRejection({
            message: failure.message,
            usages: failure.usages,
            bots: failure.bots,
            conflict: failure.status === 409,
          });
        }
      } finally {
        setSaving(false);
      }
    },
  };
}

/** The offer of a stored draft; a draft that differs only in positions is dropped without asking. */
function draftOffer<T extends AnyDef>(key: string, saved: Edited<T>): DraftOffer | undefined {
  const draft = drafts.read(key);

  if (!draft) {
    return undefined;
  }

  const difference = compareDrafts(saved, draft);

  if (difference.same) {
    drafts.remove(key);

    return undefined;
  }

  return { savedAt: draft.savedAt, difference };
}
