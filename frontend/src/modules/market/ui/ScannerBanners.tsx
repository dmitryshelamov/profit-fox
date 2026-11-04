import { useId } from 'react';

import type { Schemas } from '@/shared/api/client';
import { groupDigits } from '@/shared/format/format';

import { ageText, isStale, utcClock } from '../model/snapshotAge';

const tones = {
  run: { panel: 'border-run/40 bg-run/10', title: 'text-run', dot: 'bg-run', bar: 'bg-run' },
  warn: { panel: 'border-warn/40 bg-warn-bg', title: 'text-warn', dot: 'bg-warn', bar: 'bg-warn' },
  err: { panel: 'border-err/40 bg-err-bg', title: 'text-err', dot: 'bg-err', bar: 'bg-err' },
} as const;

/** A panel of the state of the scanner: a title with its dot, what it means, and the progress of the history if any. */
function Panel({
  tone,
  title,
  text,
  progress,
}: {
  tone: keyof typeof tones;
  title: string;
  text: string;
  progress?: { loaded: number; total: number; suffix: string };
}) {
  const id = useId();
  const colours = tones[tone];

  return (
    <section
      role="status"
      aria-labelledby={id}
      className={`space-y-1.5 rounded-xl border px-5 py-4 ${colours.panel}`}
    >
      <p id={id} className={`flex items-center gap-2 font-semibold ${colours.title}`}>
        <span aria-hidden="true" className={`size-2 rounded-full ${colours.dot}`} />
        {title}
      </p>
      <p className="pl-4 text-sm text-muted">{text}</p>
      {progress && (
        <div className="flex items-center gap-4 pl-4">
          <div
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={progress.total}
            aria-valuenow={progress.loaded}
            aria-label="Загрузка истории"
            className="h-1.5 flex-1 overflow-hidden rounded-full bg-panel-2"
          >
            <div
              className={`h-full rounded-full ${colours.bar}`}
              style={{ width: `${(progress.loaded / progress.total) * 100}%` }}
            />
          </div>
          <p className="font-mono text-sm whitespace-nowrap">
            История: {groupDigits(String(progress.loaded))} из {groupDigits(String(progress.total))}{' '}
            инструментов{progress.suffix}
          </p>
        </div>
      )}
    </section>
  );
}

/**
 * What the scanner says about its own state: the history still loading, or paused by the rate limit of Bybit, and data
 * gone stale. The time it would take to load the rest is not shown: the state of the scanner does not tell the pace.
 */
export function ScannerBanners({
  market,
  now,
}: {
  market: Schemas['MarketResponse'];
  now: number;
}) {
  const { history, restPausedUntil, snapshotAt } = market;
  const loaded = Number(history.loaded);
  const total = Number(history.total);
  const loading = loaded < total;

  return (
    <>
      {restPausedUntil !== null ? (
        <Panel
          tone="warn"
          title={`Bybit ограничил запросы — догрузка истории на паузе до ${utcClock(restPausedUntil)}`}
          text="Живые данные обновляются как обычно: свечи, OI, фандинг и спред. Догрузка продолжится сама, ничего делать не нужно."
          progress={loading ? { loaded, total, suffix: ', пауза' } : undefined}
        />
      ) : (
        loading && (
          <Panel
            tone="run"
            title="Загружается история за 90 дней"
            text="Новые минутные свечи уже идут по всем парам. Пока окно метрики не набрано, в ячейке «—»: 5м и 15м уже есть, 1ч, 4ч и 24ч появятся после загрузки истории пары."
            progress={{ loaded, total, suffix: '' }}
          />
        )
      )}
      {snapshotAt !== null && isStale(snapshotAt, now) && (
        <Panel
          tone="err"
          title={`Данные устарели: последнее обновление ${ageText(snapshotAt, now)}, сканер не отвечает`}
          text={`Значения ниже — на ${utcClock(snapshotAt)} и не меняются. Страница проверяет снова каждые 15 с; когда сканер ответит, баннер исчезнет сам.`}
        />
      )}
    </>
  );
}
