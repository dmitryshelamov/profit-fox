# Code conventions

Layout, naming and code style the owner expects in `backend/`. Where a check enforces a rule (`.editorconfig`,
`backend/.globalconfig`, `backend/Directory.Build.targets`, the banned symbols, `.githooks/pre-commit`, CI), the check is the
source of truth.

## Reference repositories

- **profit-fox-market-data** (`~/RiderProjects/profit-fox-market-data`): the layers of a service (Domain, Application,
  Infrastructure, Presentation), its `docs/conventions.md`, packages and their versions, tests. The Bot zone follows it unless
  this file says otherwise.
- **profit-fox-v2** (`~/RiderProjects/profit-fox-v2`): the old project. Its specs `strategy-graph`, `strategy-editor`,
  `strategy-library` and `strategy-execution` are the starting point of the graph; the shared strategy state machine and the
  safety of live sessions are worth carrying over.

A library neither of them uses is a question to the owner, with a recommendation, not a choice to make alone.

## Layout

- `backend/src/<zone>/ProfitFox.<Zone>.<Part>`: zones `engine`, `bot`, `bench`; the folder is lowercase, the project name is
  PascalCase.
- A project references only what `backend/Directory.Build.targets` lists for it. A new project gets its line there in the same
  change.
- Engine projects (`backend/src/engine`) also ban the system clock, randomness without a seed, files, the console and HTTP
  (`backend/src/engine/BannedSymbols.Engine.txt`): time comes from the candles, everything else from the caller.
- Bench data lives under `bench/` at the root: `hypotheses/H<NNNN>-<name>/hypothesis.json`, `ledger.jsonl`, `reports/`.

## Files and code style

- One top-level type per file, named after the type; namespaces follow folders.
- In a type: properties first, then constructors, then methods.
- An XML doc `<summary>` is always on three or more lines, the text indented by 5 spaces, even for one sentence.
- `var` only when the type is evident from the right side; the explicit type otherwise. Explicit `new Foo(...)`, never
  `new(...)`.
- A statement stays on one line while it fits `max_line_length`, 170 characters; when it does not, it is broken after the opening
  bracket, an argument a line.
- In a call with several arguments, a literal argument that is a number, `true`/`false`, `null` or `default` is named.
- Every attribute is in brackets of its own; attributes on one line have a space between them: `[FromQuery] [BindRequired]`.
- Time is UTC only: `DateTime.Now`, `DateTimeOffset.Now` and parsing without a culture are banned everywhere
  (`backend/src/BannedSymbols.txt`).

## Tests

- NUnit with the constraint model (`Assert.That`, `Assert.Multiple`).
- Tests mirror the folders of `src`.
- A node of Engine is tested on a reference dataset: given candles, the expected values bar by bar, and a check that cutting the
  data at a moment does not change the values before it.
