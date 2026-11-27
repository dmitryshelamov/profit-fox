import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import type { RequestError } from '@/api/client';
import { getScannerState, setSignalLabel } from '@/api/ideas';
import type { SignalListItem } from '@/api/signals';
import { burstLabel } from '@/pages/ideas/IdeaChoices';
import { timeframes } from '@/preview/preview';
import { Badge } from '@/shared/ui/Badge';
import { Button } from '@/shared/ui/Button';
import { Dialog } from '@/shared/ui/Dialog';
import { SelectField } from '@/shared/ui/SelectField';
import { Switch } from '@/shared/ui/Switch';
import { Td } from '@/shared/ui/Table';
import { TextField } from '@/shared/ui/TextField';

/** The longest text of a label, in signs as the eye sees them. */
export const labelLength = 12;

/**
 * The label of a signal in its row (spec idea-labels «Метка в списке сигналов»): the switch and the badge of its text
 * and timeframe, or «нет». The badge opens the window of the switch, the text and the timeframe; turning on a signal
 * without a label opens it too.
 */
export function SignalLabelCell({ signal }: { readonly signal: SignalListItem }) {
  const queries = useQueryClient();
  const [open, setOpen] = useState(false);
  const label = signal.label ?? null;
  const on = label?.enabled === true;

  const toggle = async (next: boolean) => {
    if (label === null || label.text === '') {
      setOpen(true);
      return;
    }

    const result = await setSignalLabel(signal.name, next, label.text, label.timeframe);
    if (!result.ok) {
      setOpen(true);
    }

    await queries.invalidateQueries({ queryKey: ['signals'] });
  };

  return (
    <span className="flex items-center gap-2">
      <Switch label={`Метка ${signal.name}`} checked={on} onChange={(next) => void toggle(next)} />
      <button
        type="button"
        aria-label={`Метка на идеях ${signal.name}`}
        className="flex cursor-pointer items-center gap-1.5"
        onClick={() => setOpen(true)}
      >
        {on && label ? (
          <>
            <Badge tone="muted">{label.text}</Badge>
            <span className="font-mono text-[11px] text-muted">{label.timeframe}</span>
          </>
        ) : (
          <span className="text-xs text-muted hover:text-text">нет</span>
        )}
      </button>
      {open && <LabelWindow signal={signal} onClose={() => setOpen(false)} />}
    </span>
  );
}

function LabelWindow({
  signal,
  onClose,
}: {
  readonly signal: SignalListItem;
  readonly onClose: () => void;
}) {
  const queries = useQueryClient();
  const [enabled, setEnabled] = useState(signal.label?.enabled ?? true);
  const [text, setText] = useState(signal.label?.text ?? '');
  const [timeframe, setTimeframe] = useState(signal.label?.timeframe ?? '4h');
  const [errors, setErrors] = useState<readonly RequestError[]>([]);

  const save = async () => {
    const result = await setSignalLabel(signal.name, enabled, text, timeframe);
    if (!result.ok) {
      setErrors(result.errors);
      return;
    }

    await queries.invalidateQueries({ queryKey: ['signals'] });
    onClose();
  };

  return (
    <Dialog label={`Метка ${signal.name}`} className="w-[440px]" onClose={onClose}>
      <span className="flex items-center gap-3">
        <h2 className="text-[15px] font-bold">Метка · {signal.name}</h2>
        <span className="ml-auto">
          <Switch label="Метка включена" checked={enabled} onChange={setEnabled} />
        </span>
      </span>
      <span className="text-[11px] font-semibold text-muted uppercase">Текст метки</span>
      <span className="flex items-center gap-3">
        <TextField
          aria-label="Текст метки"
          className="w-[200px]"
          value={text}
          onChange={(event) => setText(event.target.value)}
        />
        <SelectField
          aria-label="Таймфрейм метки"
          mono
          className="w-[90px]"
          value={timeframe}
          onChange={(event) => setTimeframe(event.target.value)}
        >
          {timeframes.map((item) => (
            <option key={item} value={item}>
              {item}
            </option>
          ))}
        </SelectField>
        <span className="text-xs text-muted">до {labelLength} знаков</span>
      </span>
      <p className="text-xs text-muted">
        Метка ставится идее, если сигнал выполнен на последней закрытой свече таймфрейма метки.
      </p>
      {errors.length > 0 && (
        <ul role="alert" aria-label="Отказ" className="flex flex-col gap-1 text-err">
          {errors.map((error) => (
            <li key={`${error.code}:${error.field ?? ''}:${error.node ?? ''}`}>{error.reason}</li>
          ))}
        </ul>
      )}
      <span className="flex justify-end gap-3">
        <Button onClick={onClose}>Отмена</Button>
        <Button variant="primary" onClick={() => void save()}>
          Сохранить
        </Button>
      </span>
    </Dialog>
  );
}

/**
 * The built-in label «⚡ всплеск» as the last row of the signals (spec idea-labels «Строка всплеска»): its thresholds by
 * timeframe, counted by the scanner; it has no switch and no editor.
 */
export function BurstRow({ columns }: { readonly columns: number }) {
  const state = useQuery({ queryKey: ['scanner-state'], queryFn: getScannerState });
  const thresholds = Object.entries(state.data?.burst ?? {})
    .sort(
      ([a], [b]) =>
        timeframes.indexOf(a as (typeof timeframes)[number]) -
        timeframes.indexOf(b as (typeof timeframes)[number]),
    )
    .map(([timeframe, count]) => `${timeframe} от ${String(count)}`)
    .join(' · ');

  return (
    <tr aria-label="Встроенная метка всплеск" className="h-14 border-t border-line">
      <Td>
        <span className="font-mono font-medium text-muted">всплеск</span>
      </Td>
      <Td className="text-muted">Сработала вместе с другими парами той же стороны</Td>
      <Td className="text-xs text-muted">встроенная</Td>
      <Td>
        <span className="flex flex-col gap-1">
          <Badge tone="muted">{burstLabel}</Badge>
          {thresholds !== '' && (
            <span className="font-mono text-[11px] text-muted">{thresholds}</span>
          )}
        </span>
      </Td>
      {Array.from({ length: columns - 1 }, (_, index) =>
        index === 0 ? (
          <Td key={index} className="text-xs text-muted">
            считает сканер
          </Td>
        ) : (
          <Td key={index} />
        ),
      )}
      <Td />
    </tr>
  );
}
