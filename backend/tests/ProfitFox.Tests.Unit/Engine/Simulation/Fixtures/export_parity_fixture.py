"""Exports the parity fixture of trade plan outcomes: synthetic minute paths of profit-fox-bench, entries, and the outcomes
`simulate` of `outcome.py` gives on them. Synthetic data only: no market data is read and no outcome of a hypothesis is
computed.

Run from the root of profit-fox-bench:
    uv run python <path to this file> <output file>

On the paths of `synthetic.py` the open of a minute is the close of the one before, so the time exit of Python (the close
of the last minute of the horizon) and of the order simulator (the open of the minute at the end, docs/adr/0012) agree.
The minute at the end of each horizon is therefore kept with trades. The paths vary that:
- P000: entries every 8 h; at some, the first minutes have no trades (entry on a later minute), and at one, the whole
  horizon but its end has none (no trade);
- P001: entries every 4 h; a gap in the candle coverage inside one horizon, and one horizon past the end of the path;
- P002: entries every 4 h; the funding payments are known only up to the middle of the path; random rates of both signs.
"""

import json
import sys
from dataclasses import replace
from datetime import datetime, timezone
from pathlib import Path

import numpy as np

from profit_fox_bench.outcome import STATUS_NAMES, Costs, PairPath, Plan, simulate
from profit_fox_bench.synthetic import SyntheticMarket

MINUTE_MS = 60_000
HORIZON = 240
COSTS = Costs(taker_fee=0.00055, slippage=0.0005, funding=True)


def iso(milliseconds: int) -> str:
    return datetime.fromtimestamp(milliseconds / 1000, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def number(value: float) -> str | None:
    return None if np.isnan(value) else repr(float(value))


def runs(flags: np.ndarray) -> list[tuple[int, int]]:
    """The [start, end) minute spans where the flags are True."""
    spans, start = [], None
    for index, flag in enumerate(list(flags) + [False]):
        if flag and start is None:
            start = index
        elif not flag and start is not None:
            spans.append((start, index))
            start = None
    return spans


def export(path: PairPath, entries: np.ndarray) -> dict:
    outcomes = {}
    for name, side in (("short", -1), ("long", +1)):
        result = simulate(path, entries, side, Plan(horizon_minutes=HORIZON), COSTS).as_dict()
        outcomes[name] = [
            {
                "status": STATUS_NAMES[int(result["status"][i])],
                "entry_time": iso(int(result["entry_ms"][i])) if result["entry_ms"][i] >= 0 else None,
                "exit_time": iso(int(result["exit_ms"][i])) if result["exit_ms"][i] >= 0 else None,
                **{key: number(result[key][i]) for key in ("entry_price", "exit_price", "gross", "slippage", "fees", "funding", "net")},
            }
            for i in range(len(entries))
        ]
    minutes = [
        [index, number(path.open[index]), number(path.high[index]), number(path.low[index]), number(path.close[index])]
        for index in range(path.minutes)
        if path.covered[index] and not np.isnan(path.open[index])
    ]
    return {
        "instrument": path.symbol,
        "start": iso(path.start_ms),
        "minutes": minutes,
        "coverage": [[iso(path.start_ms + a * MINUTE_MS), iso(path.start_ms + b * MINUTE_MS)] for a, b in runs(path.covered)],
        "payments": [[iso(int(t)), repr(float(r))] for t, r in zip(path.funding_ts, path.funding_rate)],
        "funding_coverage": [iso(path.funding_from), iso(path.funding_until)],
        "entries": [iso(int(e)) for e in entries],
        "outcomes": outcomes,
    }


def rebuilt(path: PairPath, **changes) -> PairPath:
    """A copy of the path with changed arrays; PairPath computes its funding sums when it is built."""
    fields = dict(
        symbol=path.symbol, start_ms=path.start_ms, open=path.open.copy(), high=path.high.copy(), low=path.low.copy(),
        close=path.close.copy(), covered=path.covered.copy(), funding_ts=path.funding_ts, funding_rate=path.funding_rate,
        funding_from=path.funding_from, funding_until=path.funding_until,
    )
    fields.update(changes)
    return PairPath(**fields)


def main(out: Path) -> None:
    market = SyntheticMarket(pairs=3, minutes=3 * 1440, seed=7)
    loader = market.loader()
    start = market.start_ms
    paths = []

    # P000: entries at 04:00, 12:00, 20:00 so no entry minute is the end minute of another trade.
    p = loader("P000USDT")
    entries = np.arange(240, p.minutes - HORIZON - 1, 480)
    opens, highs, lows, closes = p.open.copy(), p.high.copy(), p.low.copy(), p.close.copy()
    for number_, entry in enumerate(entries):
        knocked = {1: 1, 3: 2, 5: 17}.get(number_, 0)
        if number_ == 7:
            knocked = HORIZON  # no trade over the whole horizon; its end minute keeps trades
        for array in (opens, highs, lows, closes):
            array[entry : entry + knocked] = np.nan
    p = rebuilt(p, open=opens, high=highs, low=lows, close=closes)
    paths.append(export(p, start + entries * MINUTE_MS))

    # P001: a gap of 10 minutes inside the third horizon; the last entry runs past the end of the path.
    p = loader("P001USDT")
    covered = p.covered.copy()
    covered[2 * 240 + 100 : 2 * 240 + 110] = False
    p = rebuilt(p, covered=covered)
    entries = np.append(np.arange(0, p.minutes - HORIZON - 1, 240), p.minutes - 100)
    paths.append(export(p, start + entries * MINUTE_MS))

    # P002: payments known up to the middle; random rates of both signs.
    p = loader("P002USDT")
    rates = np.round(np.random.default_rng(11).normal(0.0001, 0.0006, len(p.funding_ts)), 8)
    p = rebuilt(p, funding_rate=rates, funding_until=start + (p.minutes // 2) * MINUTE_MS)
    entries = np.arange(0, p.minutes - HORIZON - 1, 240)
    paths.append(export(p, start + entries * MINUTE_MS))

    fixture = {"taker_fee": repr(COSTS.taker_fee), "slippage": repr(COSTS.slippage), "horizon_minutes": HORIZON, "paths": paths}
    out.write_text(json.dumps(fixture, separators=(",", ":")))
    statuses = {}
    for path in paths:
        for side in path["outcomes"].values():
            for outcome in side:
                statuses[outcome["status"]] = statuses.get(outcome["status"], 0) + 1
    print(f"{len(paths)} paths, outcomes by status: {statuses}")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
