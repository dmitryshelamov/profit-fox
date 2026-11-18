"""Exports the fixture of trade outcomes: synthetic minute paths, funding payments, trades and the outcomes `simulate`
of the Python bench (`outcome.py`) gives on them. Synthetic data only: no market history.

Run from the root of profit-fox-bench:
    uv run python <path to this file> <output directory>

Four paths of 3000 minutes from 2026-01-05: a random walk with gaps, minutes without trades, path 1 with funding known
only for the first 2500 minutes, path 2 with an uncovered stretch, path 3 without funding (spot). Each path gets 300
trades at random minutes with random sides and plans.
"""

import sys
from datetime import datetime, timezone
from pathlib import Path

import numpy as np

from profit_fox_bench.outcome import Costs, PairPath, Plan, simulate

START_MS = int(datetime(2026, 1, 5, tzinfo=timezone.utc).timestamp() * 1000)
MINUTE_MS = 60_000
MINUTES = 3000
PLANS = [Plan(240), Plan(240, 0.01, 0.02), Plan(60, 0.004, 0.004), Plan(600, None, 0.01), Plan(30, 0.002, None)]


def text(value) -> str:
    if value is None:
        return ""
    if isinstance(value, (float, np.floating)):
        return "NaN" if np.isnan(value) else repr(float(value))
    return str(value)


def path_of(number: int, rng: np.random.Generator) -> PairPath:
    close = np.empty(MINUTES)
    open_ = np.empty(MINUTES)
    price = 100.0 * (1 + number)
    for minute in range(MINUTES):
        jump = rng.choice([0.99, 1.01]) if rng.random() < 0.01 else 1.0
        open_[minute] = price * jump
        close[minute] = open_[minute] * np.exp(rng.normal(0, 0.002))
        price = close[minute]
    high = np.maximum(open_, close) * (1 + np.abs(rng.normal(0, 0.001, MINUTES)))
    low = np.minimum(open_, close) * (1 - np.abs(rng.normal(0, 0.001, MINUTES)))
    quiet = rng.random(MINUTES) < 0.05
    for array in (open_, high, low, close):
        array[quiet] = np.nan
    covered = np.ones(MINUTES, dtype=bool)
    if number == 2:
        covered[1500:1560] = False
    funding_ts = START_MS + np.arange(480, MINUTES * 2, 480) * MINUTE_MS
    funding_rate = rng.normal(0.0001, 0.0005, len(funding_ts))
    if number == 3:
        return PairPath(f"P{number}", START_MS, open_, high, low, close, covered, np.array([], dtype=np.int64), np.array([]))
    until = START_MS + (2500 if number == 1 else MINUTES * 2) * MINUTE_MS
    return PairPath(f"P{number}", START_MS, open_, high, low, close, covered, funding_ts, funding_rate, START_MS, until)


def main(out: Path) -> None:
    rng = np.random.default_rng(20261118)
    out.mkdir(parents=True, exist_ok=True)
    paths = [path_of(number, rng) for number in range(4)]
    with open(out / "paths.csv", "w") as file:
        file.write("path,start_ms,minutes,funding_from_ms,funding_until_ms\n")
        for number, path in enumerate(paths):
            file.write(f"{number},{path.start_ms},{path.minutes},{text(path.funding_from)},{text(path.funding_until)}\n")
    with open(out / "minutes.csv", "w") as file:
        file.write("path,minute,open,high,low,close,covered\n")
        for number, path in enumerate(paths):
            for minute in range(path.minutes):
                file.write(f"{number},{minute},{text(path.open[minute])},{text(path.high[minute])},{text(path.low[minute])},"
                           f"{text(path.close[minute])},{int(path.covered[minute])}\n")
    with open(out / "funding.csv", "w") as file:
        file.write("path,time_ms,rate\n")
        for number, path in enumerate(paths):
            for time, rate in zip(path.funding_ts, path.funding_rate):
                file.write(f"{number},{time},{text(rate)}\n")
    fields = ["status", "exit_reason", "ambiguous", "entry_ms", "exit_ms", "entry_price", "exit_price", "gross", "fees",
              "slippage", "funding", "net", "r"]
    count = 0
    with open(out / "trades.csv", "w") as file:
        file.write("path,entry_ms,side,horizon,stop,target," + ",".join(fields) + "\n")
        for number, path in enumerate(paths):
            for _ in range(300):
                entry = START_MS + int(rng.integers(-30, MINUTES - 20)) * MINUTE_MS
                side = int(rng.choice([1, -1]))
                plan = PLANS[int(rng.integers(0, len(PLANS)))]
                result = simulate(path, np.array([entry]), side, plan, Costs()).as_dict()
                row = [number, entry, side, plan.horizon_minutes, text(plan.stop), text(plan.target)]
                row += [text(result[name][0].item() if hasattr(result[name][0], "item") else result[name][0]) for name in fields]
                file.write(",".join(map(str, row)) + "\n")
                count += 1
    print(f"{len(paths)} paths, {count} trades")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
