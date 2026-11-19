# profit-fox

## Предпросмотр сигнала

Страница показывает, где граф сигнала срабатывает на паре в разведке (до 2026-05-15 включительно): график свечей с
метками событий и их список. Вердикта, исходов сделок и строк в журнале нет. Спецификация —
`openspec/specs/signal-preview` (пока изменение не в архиве — `openspec/changes/add-signal-preview`).

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

Оба процесса есть в `.claude/launch.json` (`bench-api` и `frontend`) для превью в Claude Code.

Настройки API в `backend/src/bench/ProfitFox.Bench.Presentation/appsettings.json`:

- `MarketData:GrpcUrl`, `MarketData:RestUrl` — адреса сервиса данных;
- `Bench:Folder` — папка `bench/` с гипотезами, по умолчанию `bench/` в корне репозитория.

Их можно задать и переменными окружения: `MarketData__GrpcUrl`, `MarketData__RestUrl`, `Bench__Folder`.

Ответ на одну пару идёт около 12 с: столько сервис данных отдаёт свечи и фандинг с начала данных (замер в
`openspec/changes/add-signal-preview/design.md`).

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
