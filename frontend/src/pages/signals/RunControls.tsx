import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import type { Hypothesis } from '@/api/client';
import { enqueueRun, type RunAvailability } from '@/api/runs';
import { Button } from '@/shared/ui/Button';
import { SelectField } from '@/shared/ui/SelectField';

import { kindLabel, periodLabel, periodOrder } from '../runs/labels';

// A parity run is blind and reads only exploration (spec hypothesis-bench «Сверочный прогон»).
const parityPeriod = 'exploration';

/**
 * The state of «Прогнать» and «Сверка» of one hypothesis (spec bench-run-queue «Кнопки прогона в списке гипотез»).
 * What the ledger rules allow comes from the API; a queued run opens the section «Прогоны», a refusal stays on the
 * list with its reason.
 */
export function useRunLaunch(hypothesis: Hypothesis) {
  const navigate = useNavigate();
  const queries = useQueryClient();
  const available = (period: string, kind: string): RunAvailability | undefined =>
    hypothesis.runs.find((run) => run.period === period && run.kind === kind);
  const verdictOpen = (period: string) => available(period, 'verdict')?.canRun === true;

  const [chosen, setChosen] = useState<string | null>(null);
  const [refusal, setRefusal] = useState<{ reason: string; runId: string | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const period = chosen ?? periodOrder.find(verdictOpen) ?? periodOrder[0];

  const reasons = hypothesis.runs
    .filter(
      (run) => !run.canRun && run.reason && (run.kind === 'verdict' || run.period === parityPeriod),
    )
    .sort(
      (a, b) =>
        periodOrder.indexOf(a.period as never) - periodOrder.indexOf(b.period as never) ||
        a.kind.localeCompare(b.kind),
    );

  const enqueue = async (kind: 'verdict' | 'parity') => {
    setBusy(true);
    setRefusal(null);
    try {
      const outcome = await enqueueRun({
        hypothesis: hypothesis.id,
        version: Number(hypothesis.version),
        period: kind === 'parity' ? parityPeriod : period,
        kind,
      });
      if (!outcome.ok) {
        setRefusal({ reason: outcome.reason, runId: outcome.runId });
        return;
      }

      await queries.invalidateQueries({ queryKey: ['runs'] });
      await queries.invalidateQueries({ queryKey: ['hypotheses'] });
      await navigate({ to: '/runs' });
    } catch (problem) {
      setRefusal({
        reason: problem instanceof Error ? problem.message : String(problem),
        runId: null,
      });
    } finally {
      setBusy(false);
    }
  };

  return {
    period,
    choose: setChosen,
    verdictOpen,
    parityOpen: available(parityPeriod, 'parity')?.canRun === true,
    busy,
    reasons,
    refusal,
    enqueue,
  };
}

export type RunLaunch = ReturnType<typeof useRunLaunch>;

/** The choice of the period, «Прогнать» and «Сверка»; a period that is not allowed is disabled. */
export function RunControls({
  hypothesis,
  launch,
}: {
  readonly hypothesis: Hypothesis;
  readonly launch: RunLaunch;
}) {
  return (
    <div className="flex items-center justify-end gap-2">
      <SelectField
        aria-label={`Период ${hypothesis.id}`}
        className="w-[130px]"
        value={launch.period}
        onChange={(event) => launch.choose(event.target.value)}
      >
        {periodOrder.map((option) => (
          <option key={option} value={option} disabled={!launch.verdictOpen(option)}>
            {periodLabel(option)}
          </option>
        ))}
      </SelectField>
      <Button
        aria-label={`Прогнать ${hypothesis.id}`}
        disabled={launch.busy || !launch.verdictOpen(launch.period)}
        onClick={() => void launch.enqueue('verdict')}
      >
        Прогнать
      </Button>
      <Button
        aria-label={`Сверка ${hypothesis.id}`}
        disabled={launch.busy || !launch.parityOpen}
        onClick={() => void launch.enqueue('parity')}
      >
        Сверка
      </Button>
    </div>
  );
}

/** Why a period is closed, by the words of the API, and the refusal of the last queueing; nothing when there is neither. */
export function RunNotes({
  hypothesis,
  launch,
}: {
  readonly hypothesis: Hypothesis;
  readonly launch: RunLaunch;
}) {
  const { reasons, refusal } = launch;
  if (reasons.length === 0 && refusal === null) {
    return null;
  }

  return (
    <div className="flex flex-col gap-1.5 text-xs">
      {reasons.length > 0 && (
        <ul
          aria-label={`Почему нельзя ${hypothesis.id}`}
          className="flex flex-col gap-1 text-muted"
        >
          {reasons.map((run) => (
            <li key={`${run.period}-${run.kind}`} className="flex items-baseline gap-2">
              <span aria-hidden="true" className="text-faint">
                ⓘ
              </span>
              <span>
                {periodLabel(run.period)}, {kindLabel(run.kind)}: {run.reason}
                {run.runId && (
                  <>
                    {' '}
                    <Link
                      to="/runs/$id"
                      params={{ id: run.runId }}
                      className="text-accent hover:underline"
                    >
                      прогон
                    </Link>
                  </>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}
      {refusal && (
        <p role="alert" className="text-err">
          {refusal.reason}
          {refusal.runId && (
            <>
              {' '}
              <Link
                to="/runs/$id"
                params={{ id: refusal.runId }}
                className="text-accent hover:underline"
              >
                прогон
              </Link>
            </>
          )}
        </p>
      )}
    </div>
  );
}
