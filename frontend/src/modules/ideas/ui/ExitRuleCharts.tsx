import type { Schemas } from '@/shared/api/client';
import { shiftDecimal } from '@/shared/format/decimal';
import { roundDecimal } from '@/shared/format/format';

import { percentUp } from '../model/stats';

type Life = Schemas['ExitRuleLifeResponse'];
type Mfe = Schemas['ExitRuleMfeResponse'];

const width = 520;
const height = 200;
const pad = { left: 44, right: 12, top: 14, bottom: 26 };
const plotWidth = width - pad.left - pad.right;
const plotHeight = height - pad.top - pad.bottom;

// Decimal strings become numbers here only to be drawn.
const scale = (value: number, from: number, to: number, length: number) =>
  to === from ? length / 2 : ((value - from) / (to - from)) * length;

function line(points: readonly (readonly [number, number])[]): string {
  return points
    .map(([x, y], index) => `${index === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`)
    .join(' ');
}

const percent = (share: string) => `${roundDecimal(shiftDecimal(share, 2), 0)} %`;

/** The expectancy per entry and per signal at every lifetime of the grid, on a logarithmic scale of candles, with the lifetimes now and chosen. */
export function LifeChart({
  lives,
  current,
  chosen,
}: {
  lives: readonly Life[];
  current: number;
  chosen: number;
}) {
  const shown = lives.filter((life) => life.expectancy !== null);
  const values = shown.flatMap((life) => [Number(life.expectancy), Number(life.signalExpectancy)]);
  const low = Math.min(0, ...values);
  const high = Math.max(0, ...values);
  const first = Math.log2(Number(lives[0]?.candles ?? 1));
  const last = Math.log2(Number(lives[lives.length - 1]?.candles ?? 1));
  const x = (candles: number) => pad.left + scale(Math.log2(candles), first, last, plotWidth);
  const y = (value: number) => pad.top + plotHeight - scale(value, low, high, plotHeight);

  return (
    <svg
      role="img"
      aria-label="Ожидание от срока"
      viewBox={`0 0 ${width} ${height}`}
      className="w-full"
    >
      <line x1={pad.left} x2={width - pad.right} y1={y(0)} y2={y(0)} className="stroke-line" />
      <path
        d={line(shown.map((life) => [x(Number(life.candles)), y(Number(life.expectancy))]))}
        fill="none"
        className="stroke-series-1"
        strokeWidth={2}
      />
      <path
        d={line(shown.map((life) => [x(Number(life.candles)), y(Number(life.signalExpectancy))]))}
        fill="none"
        className="stroke-series-2"
        strokeWidth={2}
      />
      <Mark x={x(current)} label={`сейчас ${current}`} tone="stroke-faint" />
      <Mark x={x(chosen)} label={`выбрано ${chosen}`} tone="stroke-accent" />
      {lives.map((life) => (
        <text
          key={life.candles}
          x={x(Number(life.candles))}
          y={height - 8}
          textAnchor="middle"
          className="fill-faint text-[9px]"
        >
          {life.candles}
        </text>
      ))}
      <text x={4} y={y(high) + 4} className="fill-faint text-[9px]">
        {high.toFixed(2)} R
      </text>
      <text x={4} y={y(low)} className="fill-faint text-[9px]">
        {low.toFixed(2)} R
      </text>
    </svg>
  );
}

/**
 * The share of the entries that reached each level before the stop and the end of the lifetime, beside the break-even of a target of
 * that level, dashed; with the targets now and chosen, none for «Уровни».
 */
export function MfeChart({
  points,
  current,
  chosen,
}: {
  points: readonly Mfe[];
  current: string | null;
  chosen: string | null;
}) {
  const x = (r: number) => pad.left + scale(r, 0.5, 5, plotWidth);
  const y = (share: number) => pad.top + plotHeight - share * plotHeight;
  const at =
    chosen === null ? undefined : points.find((point) => Number(point.r) === Number(chosen));

  return (
    <svg role="img" aria-label="Кривая MFE" viewBox={`0 0 ${width} ${height}`} className="w-full">
      <line x1={pad.left} x2={width - pad.right} y1={y(0)} y2={y(0)} className="stroke-line" />
      <path
        d={line(points.map((point) => [x(Number(point.r)), y(Number(point.reached))]))}
        fill="none"
        className="stroke-series-1"
        strokeWidth={2}
      />
      <path
        d={line(points.map((point) => [x(Number(point.r)), y(Number(point.breakEven))]))}
        fill="none"
        className="stroke-muted"
        strokeDasharray="4 3"
        strokeWidth={1.5}
      />
      {current !== null && (
        <Mark x={x(Number(current))} label={`сейчас ${current}R`} tone="stroke-faint" />
      )}
      {at && (
        <Mark
          x={x(Number(at.r))}
          label={`цель ${at.r}R: дошли ${percent(at.reached)}, нужно ${percentUp(at.breakEven)} %`}
          tone="stroke-accent"
        />
      )}
      {points.map((point) => (
        <text
          key={point.r}
          x={x(Number(point.r))}
          y={height - 8}
          textAnchor="middle"
          className="fill-faint text-[9px]"
        >
          {point.r}
        </text>
      ))}
    </svg>
  );
}

function Mark({ x, label, tone }: { x: number; label: string; tone: string }) {
  return (
    <g>
      <line
        x1={x}
        x2={x}
        y1={pad.top}
        y2={pad.top + plotHeight}
        className={tone}
        strokeDasharray="2 2"
      />
      <text x={Math.min(x + 4, width - 180)} y={pad.top + 10} className="fill-muted text-[10px]">
        {label}
      </text>
    </g>
  );
}
