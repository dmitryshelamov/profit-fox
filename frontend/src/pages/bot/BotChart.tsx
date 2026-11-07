import { useNavigate } from '@tanstack/react-router';
import { useMemo } from 'react';

import {
  chartIntervals,
  feedText,
  shownInterval,
  useBotCandles,
  useFormingCandle,
} from '@/modules/bots';
import { BotCandleChart, liveCandles } from '@/modules/market-chart';
import { intervalLabel } from '@/modules/market-data';
import type { Schemas } from '@/shared/api/client';
import { formatFullMoment } from '@/shared/format/format';

type Position = Schemas['LivePositionResponse'];

/**
 * The chart of the bot's instrument: closed candles on the interval of the address (the bot's own by default), the current
 * candle on the bot's interval only, its fills and active limit orders from the positions the page polls. The text rows
 * around the chart state what it draws, as its canvas gives no text.
 */
export function BotChart({
  bot,
  positions,
  asked,
}: {
  bot: { id: string; symbol: string; interval: string };
  positions: readonly Position[];
  /** The interval of the address, if any. */
  asked: string | undefined;
}) {
  const navigate = useNavigate();
  const interval = shownInterval(asked, bot.interval);
  const own = interval === bot.interval;
  const history = useBotCandles(bot.id, interval);
  const forming = useFormingCandle(bot.id);
  const live = useMemo(() => (own ? forming.live : []), [own, forming.live]);
  const orders = useMemo(() => positions.flatMap((position) => position.orders), [positions]);
  const fills = useMemo(
    () =>
      orders.flatMap((order) =>
        order.fills.map((fill) => ({
          at: fill.at,
          side: order.side as 'buy' | 'sell',
          price: fill.price,
        })),
      ),
    [orders],
  );
  const last = liveCandles(history.candles, live).at(-1) ?? history.candles.at(-1);
  const isForming = last !== undefined && 'isClosed' in last && !last.isClosed;

  return (
    <section
      aria-label="График"
      className="space-y-3 rounded-xl border border-line bg-panel px-5 py-4"
    >
      <div role="group" aria-label="Интервал" className="flex flex-wrap gap-1">
        {chartIntervals(bot.interval).map((item) => (
          <button
            key={item.value}
            type="button"
            aria-pressed={item.value === interval}
            onClick={() =>
              void navigate({
                to: '/bots/$botId',
                params: { botId: bot.id },
                search: { interval: item.own ? undefined : item.value },
              })
            }
            className="h-7 rounded-md px-2.5 text-xs font-semibold text-muted hover:bg-panel-2 aria-pressed:bg-panel-2 aria-pressed:text-text"
          >
            {item.label}
          </button>
        ))}
      </div>
      {last && (
        <div
          role="group"
          aria-label="Последняя свеча"
          className="flex flex-wrap gap-x-4 font-mono text-xs text-muted"
        >
          <span>{formatFullMoment(last.openTime)}</span>
          <span>O {last.open}</span>
          <span>H {last.high}</span>
          <span>L {last.low}</span>
          <span>C {last.close}</span>
          <span>Объём {last.volume}</span>
          {isForming && <span className="text-warn">не закрыта</span>}
        </div>
      )}
      <BotCandleChart
        history={history.candles}
        live={live}
        fills={fills}
        orders={orders}
        interval={interval}
        label={`График свечей ${bot.symbol}, ${intervalLabel(interval)}`}
        onReachStart={history.loadEarlier}
      />
      {history.isError && <p role="alert">Не удалось загрузить свечи.</p>}
      {history.isPending && <p className="text-sm text-muted">Загрузка свечей…</p>}
      {!history.isPending &&
        !history.isError &&
        history.candles.length === 0 &&
        !history.unavailable && <p className="text-sm text-muted">Свечей за период нет.</p>}
      {history.unavailable && (
        <p role="status" className="text-sm text-warn">
          История недоступна: Bybit не отвечает
        </p>
      )}
      {own ? (
        <p className="text-sm text-muted">
          Текущая свеча:{' '}
          <strong className="font-semibold text-text">{feedText(forming.feed)}</strong>
        </p>
      ) : (
        <p className="text-sm text-muted">
          На этом интервале закрытые свечи обновляются раз в минуту, текущей свечи нет: она идёт на
          интервале бота.
        </p>
      )}
    </section>
  );
}
