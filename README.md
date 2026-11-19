# profit-fox

## Конструктор сигналов

Раздел «Сигналы» собирает граф сигнала на холсте и показывает, где он срабатывает на паре в разведке (до 2026-05-15
включительно): график свечей с метками событий и их список. Вердикта, исходов сделок и строк в журнале нет.
Спецификации — `openspec/specs/signal-constructor` и `openspec/specs/signal-preview`.

- `/signals` — сохранённые сигналы и гипотезы стенда;
- `/signals/<имя>` — редактор сигнала с панелью предпросмотра внизу;
- `/signals/new/<id>` — новый сигнал, черновик до первого сохранения;
- `/hypotheses/<id>` — сигнал гипотезы только для чтения, его можно скопировать в новый сигнал.

Сигналы лежат файлами `bench/signals/<имя>.json`: граф в формате `strategy-graph` и рядом с ним расположение узлов.
API пишет эти файлы, но не коммитит их; коммит остаётся за владельцем. Незакоммиченные файлы `bench/signals/` не мешают
`bench run`. Несохранённые правки живут черновиком в браузере.

Нужны .NET 10, Node 22+ и доступ к сервису данных на soyo-02 (`192.168.8.213`, порты 5237 и 5236).

1. API стенда — `ProfitFox.Bench.Presentation` на `http://localhost:5240`:

   ```bash
   dotnet run --project backend/src/bench/ProfitFox.Bench.Presentation
   ```

2. Страница — Vite на `http://localhost:5173`, запросы `/api` уходят на API:

   ```bash
   npm --prefix frontend install
   ```

   ```bash
   npm --prefix frontend run dev
   ```

Оба процесса есть в `.claude/launch.json` (`bench-api` и `frontend`) для превью в Claude Code. API, запущенный так,
может не видеть локальную сеть; для реальных данных его лучше запускать в терминале. После сна Мака API перезапускают:
соединения с сервисом данных остаются мёртвыми.

Первый предпросмотр пары идёт 15–17 с: столько сервис данных отдаёт свечи и фандинг с начала данных. API держит в
памяти историю последних 5 пар с таймфреймом, поэтому повторный предпросмотр той же пары, в том числе с другим графом,
идёт доли секунды. Замеры — в `openspec/changes/add-signal-constructor/design.md`.

Настройки API в `backend/src/bench/ProfitFox.Bench.Presentation/appsettings.json`:

- `MarketData:GrpcUrl`, `MarketData:RestUrl` — адреса сервиса данных;
- `Bench:Folder` — папка `bench/` с гипотезами и сигналами, по умолчанию `bench/` в корне репозитория.

Их можно задать и переменными окружения: `MarketData__GrpcUrl`, `MarketData__RestUrl`, `Bench__Folder`.

Типы API для фронтенда генерируются из OpenAPI работающего API:

```bash
npm --prefix frontend run generate:api
```

## Прогон гипотезы

```bash
dotnet run --project backend/src/bench/ProfitFox.Bench.Cli -- run bench/hypotheses/H0001-funding-short-4h
```

Сверочный прогон без вердикта — `parity <папка гипотезы> --out <папка>`. Результат сверки с Python-стендом —
`bench/parity/README.md`.
