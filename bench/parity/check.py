"""Checks a dump of the C# bench against the Python bench (design of add-hypothesis-bench, decision 7).

    uv run --project ../profit-fox-bench python bench/parity/check.py <dump folder> <hypothesis folder>

The dump of `bench parity` is blind: this script compares the events, the statuses of event trades, the outcomes of the
random entries, the pseudo block means and the planned power, and never computes an event outcome. The dump of
`bench run --parity-dump` also has the event outcomes and the verdict; then they are compared too.

The Python side gets the same data the C# bench read: the 4h candles with trades (complete, as the service gives only
covered candles), the funding payments, and the minutes of each span with its coverage. Exit code 1 on any difference.
"""

import importlib.util
import json
import sys
from pathlib import Path

import numpy as np
import polars as pl

from profit_fox_bench.market import MarketData
from profit_fox_bench.outcome import Costs, PairPath, Plan, simulate
from profit_fox_bench.verdict import decide, planned_power

TOLERANCE = 1e-9
MINUTE_MS = 60_000
DURATIONS = {"m": 1, "h": 60, "d": 1440, "w": 10080}
PERIODS = {"exploration": (1672531200000, 1778889600000), "holdout": (1778889600000, 1794787200000)}
PYTHON_EVENTS = Path(__file__).resolve().parents[3] / "profit-fox-bench" / "hypotheses"
FIELDS = ["status", "exit_reason", "ambiguous", "entry_ms", "exit_ms", "entry_price", "exit_price", "gross", "fees",
          "slippage", "funding", "net", "r"]


def read(dump: Path, name: str) -> pl.DataFrame:
    path = dump / f"{name}.csv.gz"
    return pl.read_csv(path, infer_schema_length=0) if path.exists() else pl.DataFrame()


def minutes(text: str) -> int:
    return int(text[:-1]) * DURATIONS[text[-1]]


def event_function(hypothesis_id: str):
    folder = next(PYTHON_EVENTS.glob(f"{hypothesis_id}-*"))
    spec = importlib.util.spec_from_file_location(f"event_{hypothesis_id}", folder / "event.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.events


class Differences:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, text: str) -> None:
        self.items.append(text)

    def compare(self, where: str, ours: dict, theirs: dict) -> None:
        for name in FIELDS:
            a, b = ours[name], theirs[name]
            if isinstance(b, float) or isinstance(a, float):
                a, b = float(a), float(b)
                same = (np.isnan(a) and np.isnan(b)) or abs(a - b) <= TOLERANCE
            else:
                same = a == b
            if not same:
                self.add(f"{where}: {name} C# {a!r} Python {b!r}")


def outcome_row(row: dict) -> dict:
    values = {}
    for name in FIELDS:
        text = row[name]
        if name == "ambiguous":
            values[name] = text == "True"
        elif name in ("status", "exit_reason", "entry_ms", "exit_ms"):
            values[name] = int(text)
        else:
            values[name] = float("nan") if text == "NaN" else float(text)
    return values


def python_outcome(path: PairPath, entry_ms: int, side: int, plan: Plan, costs: Costs) -> dict:
    result = simulate(path, np.array([entry_ms]), side, plan, costs).as_dict()
    values = {name: result[name][0].item() if hasattr(result[name][0], "item") else result[name][0] for name in FIELDS}
    values["ambiguous"] = bool(values["ambiguous"])
    return values


def paths(dump: Path, funding: pl.DataFrame) -> dict[tuple[str, int], PairPath]:
    spans = read(dump, "spans")
    minute_rows = read(dump, "minutes")
    coverage = read(dump, "coverage")
    built = {}
    by_span = minute_rows.partition_by("instrument", "span_from_ms", as_dict=True) if minute_rows.height else {}
    for symbol, span_from, span_to in spans.iter_rows():
        start, end = int(span_from), int(span_to)
        length = (end - start) // MINUTE_MS
        prices = {column: np.full(length, np.nan) for column in ("open", "high", "low", "close")}
        rows = by_span.get((symbol, span_from))
        if rows is not None:
            index = (rows["open_ms"].cast(pl.Int64).to_numpy() - start) // MINUTE_MS
            for column in prices:
                prices[column][index] = rows[column].cast(pl.Float64).to_numpy()
        covered = np.zeros(length, dtype=bool)
        ranges = coverage.filter((pl.col("feed") == "minutes") & (pl.col("instrument") == symbol) & (pl.col("span_from_ms") == span_from))
        for _, _, _, range_from, range_until in ranges.iter_rows():
            covered[max(0, (int(range_from) - start) // MINUTE_MS): max(0, (int(range_until) - start) // MINUTE_MS)] = True
        paid = funding.filter(pl.col("instrument") == symbol)
        known = coverage.filter((pl.col("feed") == "funding") & (pl.col("instrument") == symbol))
        known = [(int(a), int(b)) for _, _, _, a, b in known.iter_rows() if int(a) <= start < int(b)]
        funding_from, funding_until = known[0] if known else (start, start)
        built[(symbol, start)] = PairPath(
            symbol, start, prices["open"], prices["high"], prices["low"], prices["close"], covered,
            paid["time_ms"].cast(pl.Int64).to_numpy(), paid["rate"].cast(pl.Float64).to_numpy(), funding_from, funding_until)
    return built


def main(dump: Path, hypothesis_folder: Path) -> int:
    hypothesis = json.loads((hypothesis_folder / "hypothesis.json").read_text())
    run = json.loads((dump / "run.json").read_text())
    side = 1 if hypothesis["plan"]["side"] == "long" else -1
    horizon = minutes(hypothesis["plan"]["horizon"])
    plan = Plan(horizon, hypothesis["plan"].get("stop"), hypothesis["plan"].get("target"))
    costs = Costs()
    measure = hypothesis["measure"]
    start_ms, end_ms = PERIODS[run["Period"]]
    last_entry = end_ms - horizon * MINUTE_MS
    differences = Differences()

    # Events: event.py on the same candles and payments.
    candles = read(dump, "signal_candles")
    funding = read(dump, "funding")
    frame = minutes(hypothesis["timeframe"]) * MINUTE_MS
    bars = candles.select(
        pl.col("instrument").alias("symbol"),
        (pl.col("open_ms").cast(pl.Int64) + frame).alias("ts"),
        pl.lit(True).alias("complete"),
        pl.when(pl.col("volume").cast(pl.Float64) > 0).then(1).otherwise(0).cast(pl.UInt32).alias("minutes_traded"))
    points = funding.select(pl.col("instrument").alias("symbol"), pl.col("time_ms").cast(pl.Int64).alias("ts"), pl.col("rate").cast(pl.Float64))
    data = MarketData(instruments=pl.DataFrame(), _bars={hypothesis["timeframe"]: bars}, _points={"funding": points})
    python_events = event_function(hypothesis["id"])(data).filter((pl.col("ts") >= start_ms) & (pl.col("ts") <= last_entry))
    expected = set(python_events.select("symbol", "ts").iter_rows())
    events = read(dump, "events")
    ours = {(symbol, int(moment)) for symbol, moment, _, _ in events.iter_rows()}
    for missing in sorted(expected - ours)[:20]:
        differences.add(f"event only in Python: {missing}")
    for extra in sorted(ours - expected)[:20]:
        differences.add(f"event only in C#: {extra}")

    # Trades: the statuses of event trades, the outcomes of random entries, and the pseudo block means.
    built = paths(dump, funding)
    block_spans = {}
    for (symbol, start) in built:
        block_spans.setdefault(symbol, []).append(start)

    def path_of(symbol: str, moment: int) -> PairPath:
        starts = [start for start in block_spans.get(symbol, []) if start <= moment]
        return built[(symbol, max(starts))]

    randoms = read(dump, "randoms")
    by_event = randoms.partition_by("instrument", "event_ms", as_dict=True, maintain_order=True) if randoms.height else {}
    pseudo_by_block: dict[int, list[float]] = {}
    checked = 0
    for symbol, moment, block, status in events.iter_rows():
        moment, block, status = int(moment), int(block), int(status)
        path = path_of(symbol, moment)
        python_status = int(simulate(path, np.array([moment]), side, plan, costs).status[0])
        if python_status != status:
            differences.add(f"{symbol} {moment}: event status C# {status} Python {python_status}")
        valid = []
        rows = by_event.get((symbol, str(moment)))
        for row in (rows.iter_rows(named=True) if rows is not None else []):
            ours_outcome = outcome_row(row)
            theirs = python_outcome(path, int(row["entry_ms_planned"]), side, plan, costs)
            differences.compare(f"{symbol} event {moment} random {row['entry_ms_planned']}", ours_outcome, theirs)
            checked += 1
            if theirs["status"] == 0:
                valid.append(theirs["r"] if measure == "r" else theirs["net"])
        if status == 0 and len(valid) >= 2:
            pseudo_by_block.setdefault(block, []).append(valid[0] - float(np.mean(valid[1:])))

    pseudo = read(dump, "pseudo_blocks")
    ours_pseudo = {int(block): float(mean) for block, mean, _ in pseudo.iter_rows()}
    theirs_pseudo = {block: float(np.mean(values)) for block, values in pseudo_by_block.items()}
    if set(ours_pseudo) != set(theirs_pseudo):
        differences.add(f"pseudo blocks differ: C# {sorted(ours_pseudo)[:5]}… Python {sorted(theirs_pseudo)[:5]}…")
    for block in sorted(set(ours_pseudo) & set(theirs_pseudo)):
        if abs(ours_pseudo[block] - theirs_pseudo[block]) > TOLERANCE:
            differences.add(f"pseudo block {block}: C# {ours_pseudo[block]!r} Python {theirs_pseudo[block]!r}")
    pseudo_means = np.array([theirs_pseudo[block] for block in sorted(theirs_pseudo)])
    power, _, required_t = planned_power(pseudo_means, hypothesis["minEffect"], run["Attempts"])
    if abs(power - run["Power"]) > TOLERANCE or abs(required_t - run["RequiredT"]) > TOLERANCE:
        differences.add(f"planned power C# {run['Power']!r}/{run['RequiredT']!r} Python {power!r}/{required_t!r}")

    # A run with a verdict: event outcomes, block means and the decision.
    outcomes = read(dump, "event_outcomes")
    if run["Status"] != "parity" and outcomes.height:
        for row in outcomes.iter_rows(named=True):
            moment = int(row["moment_ms"])
            path = path_of(row["instrument"], moment)
            theirs = python_outcome(path, moment, side, plan, costs)
            differences.compare(f"{row['instrument']} event {moment}", outcome_row(row), theirs)
        verdict = decide(None if run["T"] is None else np.array([float(m) for _, m, _ in read(dump, "blocks").iter_rows()]),
                         pseudo_means, hypothesis["minEffect"], run["Attempts"], run["Events"])
        if verdict.verdict != run["Status"] or verdict.reason != run["Reason"]:
            differences.add(f"verdict C# {run['Status']} ({run['Reason']}) Python {verdict.verdict} ({verdict.reason})")

    print(f"{run['Status']} dump: {len(ours)} events (Python {len(expected)}), {checked} random trades, "
          f"{len(ours_pseudo)} pseudo blocks, power {power:.4f}, required t {required_t:.4f}")
    if differences.items:
        print(f"{len(differences.items)} differences:")
        for item in differences.items[:50]:
            print("  " + item)
        return 1
    print("no differences")
    return 0


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1]), Path(sys.argv[2])))
