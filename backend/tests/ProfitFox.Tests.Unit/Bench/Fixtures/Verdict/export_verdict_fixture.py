"""Exports the fixture of the verdict rules: required t by attempts, power, and decisions of `verdict.py` of the Python
bench on synthetic block means. Synthetic numbers only.

Run from the root of profit-fox-bench:
    uv run python <path to this file> <output directory>
"""

import json
import sys
from pathlib import Path

import numpy as np

from profit_fox_bench.verdict import decide, power, required_t


def main(out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    with open(out / "required_t.csv", "w") as file:
        file.write("attempts,required_t\n")
        for attempts in [1, 2, 3, 5, 10, 20, 50, 100, 1000]:
            file.write(f"{attempts},{required_t(attempts)!r}\n")
    with open(out / "power.csv", "w") as file:
        file.write("effect,sd,blocks,t_needed,power\n")
        for effect, sd, blocks, t_needed in [(0.003, 0.004, 24, 3.0), (0.003, 0.01, 24, 3.0), (0.1, 0.2, 50, 3.65),
                                             (0.001, 0.002, 100, 4.2), (0.003, 0.0, 24, 3.0), (0.003, 0.004, 1, 3.0)]:
            file.write(f"{effect!r},{sd!r},{blocks},{t_needed!r},{power(effect, sd, blocks, t_needed)!r}\n")
    rng = np.random.default_rng(20261119)
    cases = []
    for number in range(40):
        blocks = int(rng.integers(5, 60))
        pseudo = rng.normal(0, rng.choice([0.002, 0.005, 0.02]), blocks)
        means = rng.normal(rng.choice([-0.002, 0.0, 0.003, 0.008]), 0.004, blocks)
        attempts = int(rng.choice([1, 1, 3, 10]))
        min_effect = float(rng.choice([0.003, 0.001, 0.01]))
        verdict = decide(means, pseudo, min_effect, attempts, events=blocks * 3)
        cases.append({"pseudo": pseudo.tolist(), "means": means.tolist(), "attempts": attempts, "min_effect": min_effect,
                      "verdict": verdict.as_dict()})
    with open(out / "decisions.json", "w") as file:
        json.dump(cases, file, indent=1)
    print(f"{len(cases)} decisions:", {v: sum(c['verdict']['verdict'] == v for c in cases) for v in ('passed', 'rejected', 'insufficient_data')})


if __name__ == "__main__":
    main(Path(sys.argv[1]))
