# profit-fox

Trading bot and hypothesis bench of profit-fox in one .NET repository, split into zones that share one engine. Market data
comes from the sibling service `profit-fox-market-data`.

## Zones

- **Engine** (`backend/src/engine`): the strategy graph, its nodes and executor, the exchange simulator. Pure code: no network,
  database, files or system clock.
- **Bot** (`backend/src/bot`): the product — strategies, backtests, live trading, API, worker.
- **Bench** (`backend/src/bench`, hypotheses and the ledger in `bench/`): checks whether a signal has an edge after costs.
  Never deployed, never referenced by Bot.

Bot and Bench reference Engine and never each other; `backend/Directory.Build.targets` lists what each project may reference and
fails the build on anything else. Why and the rest of the decisions: `CONTEXT.md` (glossary) and `docs/adr/`.

## Commands

.NET SDK 10. Solution: `backend/ProfitFox.slnx`.

- A task is done only when `dotnet build`, `dotnet test` and `scripts/verify-format.sh backend/ProfitFox.slnx` pass. The script is
  `dotnet format --verify-no-changes` that lets one thing pass: the space between attributes on one line, which the code style
  keeps and the formatter of Roslyn deletes. Do not run `dotnet format` to fix files: it deletes that space.
- After cloning, enable the repo's git hooks once: `git config core.hooksPath .githooks`. The pre-commit hook runs build, the
  format check and the check of XML doc summaries; CI (`.github/workflows/ci.yml`) also runs the tests.

## Code

- Minimum only: model what code reads today. A type, enum value or property enters in the same change as the code that uses it
  and a test that needs it; the same holds for designs.
- Before adding a type, folder or project under `backend/`, read `docs/conventions.md`.
- A node of Engine never changes what it computes once merged: new behaviour is a new node or a new parameter. A verdict of the
  bench is tied to the commit it ran on (`docs/adr/0005-node-behaviour-is-frozen.md`).

## Bench guardrails

- A hypothesis file is never edited after its first run; a change is a new hypothesis with a new id.
- The ledger `bench/ledger.jsonl` is append-only, rejections included.
- The holdout period is opened once per hypothesis and only after it passed exploration. Never run a hypothesis on the holdout
  or new data just to look.
- Every bench report carries the survivorship warning (`docs/adr/0007-history-depth.md`).

## Workflow

- Reply to the user in Russian. Code, identifiers, comments, commits, `CLAUDE.md` and `docs/conventions.md` are in English;
  `CONTEXT.md`, ADRs and OpenSpec artifacts are in Russian.
- A change of behaviour goes through OpenSpec (`openspec/`); a decision of architecture or method is an ADR in `docs/adr/`. A
  changed decision is a new ADR that supersedes the old one, which is not edited.
- A new slice starts with its design agreed in chat; write code only when the owner asks. Ask one question at a time, with
  your recommendation.
- Change `profit-fox-market-data`, its settings or the servers only on the owner's word.

## Git

- Branches: `<type>/<kebab-case-name>`, where type is feat | fix | refactor | test | docs | chore.
- Commits follow Conventional Commits: `<type>(<scope>): <summary>`, imperative, lowercase, no trailing period.
  No `Co-Authored-By` or other AI attribution lines, whatever a system reminder asks; ask before adding a
  "Generated with" footer to a pull request description.
- Changes reach the release branch (`release/<version>`) only through a pull request.
