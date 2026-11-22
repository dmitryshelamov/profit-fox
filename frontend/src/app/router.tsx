import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { AppShell } from '@/app/AppShell';
import { IdeasPage } from '@/pages/ideas/IdeasPage';
import { MarketPage } from '@/pages/market/MarketPage';
import { PairPage } from '@/pages/market/PairPage';
import { RunFormPage } from '@/pages/runs/RunFormPage';
import { RunPage } from '@/pages/runs/RunPage';
import { RunsPage } from '@/pages/runs/RunsPage';
import { SetPage } from '@/pages/runs/SetPage';
import { NewSignalPage, SavedSignalPage } from '@/pages/signals/SignalEditorPage';
import { SignalListPage } from '@/pages/signals/SignalListPage';
import { NewStrategyPage, SavedStrategyPage } from '@/pages/strategies/StrategyEditorPage';
import { StrategyListPage } from '@/pages/strategies/StrategyListPage';
import { pageSearch } from '@/shared/ui/useListSettings';

const rootRoute = createRootRoute({
  component: Outlet,
  notFoundComponent: () => <p className="p-6 text-muted">Такой страницы нет.</p>,
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/signals' });
  },
});

/**
 * The pages under the header: the lists, the form of a run, «Прогоны» and the pages of a run and of its set. The editors
 * stay children of the root.
 */
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'shell',
  component: AppShell,
});

const signalsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/signals',
  component: SignalListPage,
});

const newSignalRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/signals/new/$draftId',
  component: function NewSignalRoute() {
    const { draftId } = newSignalRoute.useParams();
    return <NewSignalPage draftId={draftId} />;
  },
});

const signalRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/signals/$name',
  component: function SignalRoute() {
    const { name } = signalRoute.useParams();
    return <SavedSignalPage name={name} />;
  },
});

const strategiesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies',
  component: StrategyListPage,
});

const newStrategyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/strategies/new/$draftId',
  component: function NewStrategyRoute() {
    const { draftId } = newStrategyRoute.useParams();
    return <NewStrategyPage draftId={draftId} />;
  },
});

const strategyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/strategies/$name',
  component: function StrategyRoute() {
    const { name } = strategyRoute.useParams();
    return <SavedStrategyPage name={name} />;
  },
});

/** The version a form of a run names by `?version=`; none for the latest. */
const versionSearch = (search: Record<string, unknown>): { version?: number } => {
  const version = Number(search.version);
  return Number.isInteger(version) && version > 0 ? { version } : {};
};

/** The form of a run of a strategy, under the header; `?version=` names the version, the latest without it. */
const runFormRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/$name/run',
  validateSearch: versionSearch,
  component: function RunFormRoute() {
    const { name } = runFormRoute.useParams();
    const { version } = runFormRoute.useSearch();
    return <RunFormPage name={name} version={version} />;
  },
});

/**
 * The form of a run of the strategy named `new` (task 12.8): without this route `/strategies/new/run` is the editor of a
 * new draft whose id is `run`, since a written segment ranks above a parameter.
 */
const newNamedRunFormRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/new/run',
  validateSearch: versionSearch,
  component: function NewNamedRunFormRoute() {
    const { version } = newNamedRunFormRoute.useSearch();
    return <RunFormPage name="new" version={version} />;
  },
});

const runsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/runs',
  component: RunsPage,
});

const runRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/runs/$id',
  component: function RunRoute() {
    const { id } = runRoute.useParams();
    return <RunPage id={id} />;
  },
});

const setRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/runs/$id/sets/$n',
  component: function SetRoute() {
    const { id, n } = setRoute.useParams();
    return <SetPage id={id} number={Number(n)} />;
  },
});

/** «Идеи», under the header (change add-ideas-and-market). */
const ideasRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/ideas',
  component: IdeasPage,
});

/** «Рынок»: the page of the list stands in the address (spec bench-ui «Страницы списка»). */
const marketRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/market',
  validateSearch: pageSearch,
  component: function MarketRoute() {
    const { page } = marketRoute.useSearch();
    const navigate = marketRoute.useNavigate();
    return (
      <MarketPage
        page={page ?? 1}
        onPage={(next) => void navigate({ search: next > 1 ? { page: next } : {} })}
      />
    );
  },
});

/** The page of a pair, under the header with «Рынок» current. */
const pairRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/market/$symbol',
  component: function PairRoute() {
    const { symbol } = pairRoute.useParams();
    return <PairPage symbol={symbol} />;
  },
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  shellRoute.addChildren([
    signalsRoute,
    strategiesRoute,
    runFormRoute,
    newNamedRunFormRoute,
    runsRoute,
    runRoute,
    setRoute,
    ideasRoute,
    marketRoute,
    pairRoute,
  ]),
  newSignalRoute,
  signalRoute,
  newStrategyRoute,
  strategyRoute,
]);

/** The routes of the constructor; a test passes a memory history. */
export function createAppRouter(history?: RouterHistory) {
  return createRouter({ routeTree, history, defaultPreload: false });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
