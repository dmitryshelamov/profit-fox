# profit-fox

Trading strategy designer, backtesting and live trading. Live trading is always on and works with
real money: tests and local runs never send real orders — use a mock or the exchange testnet only.

## Commands

.NET SDK 10. Solution: `backend/ProfitFox.sln`.

- Build: `dotnet build backend/ProfitFox.sln`
- Test: `dotnet test backend/ProfitFox.sln`
- Format: `dotnet format backend/ProfitFox.sln`

A task is done only when build, tests and `dotnet format --verify-no-changes` all pass.

After cloning, enable the repo's git hooks once: `git config core.hooksPath .githooks`.

## Domain rules

- Money, prices, volumes and fees are `decimal` only, never `double`/`float`.
- Time is UTC only (`DateTimeOffset`); never use `DateTime.Now` or local time.
- Backtests are deterministic: the same data and parameters give the same result.
  Randomness only with an explicitly passed seed; the current time never affects a calculation.
- No look-ahead: a strategy decision on bar N uses only data up to and including bar N.
  An order placed on the close of bar N fills no earlier than bar N+1.

## Architecture

- New projects, layers and external dependencies (databases, brokers, data sources) are introduced
  only through an OpenSpec design, not while implementing a task.
- One top-level type per file, named after the type. A type used only by its enclosing class may
  stay nested in it.

## Tests

- NUnit. New calculation logic follows red → green → refactor strictly, one step at a time:
  - Red: write only the test first. Run it and make sure it fails for the expected reason.
    A test that has never been red proves nothing.
  - Green: write the minimum code that makes the test pass. Code that no test requires is not
    checked by anything and falls out of the feedback loop. So nothing "for later": no extra
    validation, abstractions or handling of cases the tests do not cover. Need a new case —
    write a failing test for it first.
  - Refactor: change structure only; behavior and tests stay untouched; all tests green afterwards.
- Before finishing a task, go through your diff: every branch, condition and check in new code
  must be required by some test. If it is not — delete the code or write the test first.
- Indicators, PnL and order execution are verified on small reference datasets with
  precomputed expected results.

## Workflow

- Features go through OpenSpec, all steps in one feature branch:
  1. `/grill-me` for a non-trivial feature — use it instead of `/opsx:explore`; done when scope
     and acceptance criteria are agreed.
  2. `/opsx:propose` — builds on the decisions agreed in the conversation.
  3. The owner reviews the artifacts; revisions go through `/opsx:update`.
  4. `/opsx:apply` — done when build, tests and format pass.
  5. `/opsx:archive` before the merge request, so code and updated `openspec/specs` land in
     `main` as one squash commit.
- Record decisions in the change's `design.md`; the archived `design.md` is the decision log.
  No ADRs in `docs/adr/` for now.
- Reply to the user in Russian.
- OpenSpec artifacts are in Russian; code, identifiers, code comments and commits are in English.
- The repository is on GitLab (`gitlab.com/dmitry_shelamov/profit-fox`), not GitHub.

## Git

- Never commit or push to `main` directly: work in a branch and merge only through a GitLab merge request.
- Merge requests are merged with squash: one MR becomes one commit in `main`. The squash commit
  message comes from the MR title, so the MR title follows the commit format below.
- Branches: `<type>/<kebab-case-name>`, where type is feat | fix | refactor | test | docs | chore.
  For an OpenSpec change, use the change name: `feat/add-backtest-engine`.
- Commits follow Conventional Commits: `<type>(<scope>): <summary>` — imperative mood, lowercase,
  no trailing period, summary up to 72 characters. Scope is optional.
- One logical change per merge request; every MR passes build and tests.

## Exchange and production access

- With the owner's explicit approval in chat, the agent may use the exchange API token to work with
  the live exchange and do whatever the task requires. The token comes only from an environment
  variable; never print, log or commit it.
- With the owner's explicit approval in chat, the agent may connect to the production server and do
  whatever the task requires.
- An approval covers one specific action or task, not the rest of the session. Before acting, state
  exactly what will be done (on the exchange: account, instrument, side, size; on production: commands
  and affected services); afterwards, report what was actually done.

## Restrictions

- Do not read or change `.env.prod*`, `docker-compose.prod.yml` or the production server without
  the owner's approval (see above).
- Never commit secrets: exchange keys, tokens and passwords go only through environment variables
  or user secrets, never into code or configs in git.
