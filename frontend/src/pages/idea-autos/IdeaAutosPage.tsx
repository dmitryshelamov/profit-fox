import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { autosQuery, AutosTable, DailyStopBanner, GuardsStrip } from '@/modules/idea-autos';

/** The autos that take ideas by themselves: their order is their priority, the mode of each is switched here. */
export function IdeaAutosPage() {
  const autos = useQuery(autosQuery());

  return (
    <section className="space-y-4">
      <nav aria-label="Путь" className="text-sm text-muted">
        <Link to="/idea-trades" className="hover:underline">
          Сделки
        </Link>{' '}
        / <span className="text-faint">Автоматы</span>
      </nav>
      <header className="flex flex-wrap items-start gap-4">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">Автоматы</h1>
          <p className="text-sm text-muted">
            Автоматы берут идеи сами: у каждого свои условия, аккаунт, сумма и режим. Берут только
            идеи, родившиеся, пока автомат включён; одна сделка на пару у автомата.
          </p>
        </div>
        <Link
          to="/idea-trades/autos/new"
          className="ml-auto inline-flex h-9 items-center rounded-md bg-accent px-4 text-sm font-bold text-on-accent hover:brightness-110"
        >
          + Добавить автомат
        </Link>
      </header>
      <DailyStopBanner />
      <GuardsStrip />
      {autos.isError && <p role="alert">Не удалось загрузить автоматы.</p>}
      {autos.isPending && <p className="text-sm text-muted">Загрузка автоматов…</p>}
      {autos.data && autos.data.length === 0 && (
        <p className="rounded-xl bg-panel px-4 py-6 text-sm text-muted">
          Автоматов пока нет. Добавьте первый: он возьмёт идеи по вашим условиям без вашего участия.
        </p>
      )}
      {autos.data && autos.data.length > 0 && (
        <>
          <AutosTable items={autos.data} />
          <p className="rounded-lg bg-run/10 px-4 py-3 text-xs text-run">
            Пару на одном аккаунте берёт верхний автомат с деньгами; остальные пишут отказ «Уже есть
            сделка по паре». Режим меняется сразу, без подтверждения. Порядок строк — приоритет
            автоматов; меняется перетаскиванием за ⠿.
          </p>
        </>
      )}
    </section>
  );
}
