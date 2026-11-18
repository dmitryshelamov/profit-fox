"""Exports the parity fixture of H0001: real funding payments of the exploration period from the snapshot of
profit-fox-bench, a generated grid of 4h bars, and the events `event.py` gives on exactly that data. Events only: no
outcome of an H0001 event is computed here.

Run from the root of profit-fox-bench:
    uv run python <path to this file> <output directory>

The snapshot has bars with trades only from 2026-09, inside the holdout period, so the bars are generated
(docs/adr/0008-periods.md; design of add-strategy-graph). `event.py` reads of a bar only the pair, the close time,
`complete` and `minutes_traded`. A deterministic part of the grid is incomplete or without trades, and some pairs start
later than the window: the executor gets only the bars `event.py` keeps. Pairs: the first 20 by name with an event in
the window and the first 20 without one. Payments start 8 h before the window, so its first bars can see a rate.
"""

import importlib.util
import sys
from datetime import datetime, timezone
from pathlib import Path

import polars as pl

from profit_fox_bench.market import MarketData, Snapshot, known_at

START = datetime(2026, 1, 1, tzinfo=timezone.utc)
END = datetime(2026, 3, 1, tzinfo=timezone.utc)
BAR_MS = 4 * 3600 * 1000
PAYMENTS_BEFORE_MS = 8 * 3600 * 1000
PAIRS_EACH = 20
EVENT_FILE = Path("hypotheses/H0001-funding-short-4h/event.py")


def ms(moment: datetime) -> int:
    return int(moment.timestamp() * 1000)


def iso(milliseconds: int) -> str:
    return datetime.fromtimestamp(milliseconds / 1000, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_events():
    spec = importlib.util.spec_from_file_location("h0001_event", EVENT_FILE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.events


def grid(symbols: list[str]) -> pl.DataFrame:
    """4h bars of every pair over the window; a fixed rule from the pair's name and the bar's number drops some."""
    rows = []
    count = (ms(END) - ms(START)) // BAR_MS
    for symbol in symbols:
        seed = sum(map(ord, symbol))
        first = (seed % 5) * 6  # a pair listed up to four days into the window
        for number in range(first, count):
            open_time = ms(START) + number * BAR_MS
            complete = (number + seed) % 11 != 5
            minutes_traded = 0 if (number + seed) % 13 == 7 else 240
            rows.append((symbol, open_time, open_time + BAR_MS, complete, minutes_traded))
    return pl.DataFrame(
        rows,
        schema={"symbol": pl.String, "open_time": pl.Int64, "ts": pl.Int64, "complete": pl.Boolean, "minutes_traded": pl.UInt32},
        orient="row",
    )


def main(out: Path) -> None:
    events_of = load_events()
    snapshot = Snapshot(Path("data/snapshot"))
    funding = known_at(snapshot.points("funding"), "funding").filter(
        (pl.col("ts") >= ms(START) - PAYMENTS_BEFORE_MS) & (pl.col("ts") <= ms(END))
    )
    symbols = sorted(funding["symbol"].unique().to_list())
    bars = grid(symbols)

    def run(chosen: list[str]) -> pl.DataFrame:
        data = MarketData(
            instruments=pl.DataFrame(),
            _bars={"4h": bars.filter(pl.col("symbol").is_in(chosen))},
            _points={"funding": funding.filter(pl.col("symbol").is_in(chosen))},
        )
        return events_of(data)

    with_events = set(run(symbols)["symbol"].to_list())
    chosen = sorted(with_events)[:PAIRS_EACH] + [s for s in symbols if s not in with_events][:PAIRS_EACH]
    events = run(chosen).sort("ts", "symbol")

    kept = bars.filter(pl.col("symbol").is_in(chosen) & pl.col("complete") & (pl.col("minutes_traded") > 0)).sort("open_time", "symbol")
    paid = funding.filter(pl.col("symbol").is_in(chosen)).sort("ts", "symbol")

    out.mkdir(parents=True, exist_ok=True)
    with open(out / "bars.csv", "w") as file:
        file.write("instrument,open_time,close_time\n")
        for symbol, open_time, close_time in kept.select("symbol", "open_time", "ts").iter_rows():
            file.write(f"{symbol},{iso(open_time)},{iso(close_time)}\n")
    with open(out / "funding.csv", "w") as file:
        file.write("instrument,time,rate\n")
        for symbol, time, rate in paid.select("symbol", "ts", "rate").iter_rows():
            file.write(f"{symbol},{iso(time)},{float(rate)!r}\n")
    with open(out / "events.csv", "w") as file:
        file.write("instrument,close_time\n")
        for symbol, close_time in events.select("symbol", "ts").iter_rows():
            file.write(f"{symbol},{iso(close_time)}\n")

    print(f"{len(chosen)} pairs, {min(len(with_events), PAIRS_EACH)} with events; {kept.height} bars, {paid.height} payments, {events.height} events")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
