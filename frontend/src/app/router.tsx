import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { AppShell } from '@/app/AppShell';
import { RunFormPage } from '@/pages/runs/RunFormPage';
import { RunPage } from '@/pages/runs/RunPage';
import { RunsPage } from '@/pages/runs/RunsPage';
import { SetPage } from '@/pages/runs/SetPage';
import { NewSignalPage, SavedSignalPage } from '@/pages/signals/SignalEditorPage';
import { SignalListPage } from '@/pages/signals/SignalListPage';
import { NewStrategyPage, SavedStrategyPage } from '@/pages/strategies/StrategyEditorPage';
import { StrategyListPage } from '@/pages/strategies/StrategyListPage';

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

/** The form of a run of a strategy, under the header; `?version=` names the version, the latest without it. */
const runFormRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies/$name/run',
  validateSearch: (search: Record<string, unknown>): { version?: number } => {
    const version = Number(search.version);
    return Number.isInteger(version) && version > 0 ? { version } : {};
  },
  component: function RunFormRoute() {
    const { name } = runFormRoute.useParams();
    const { version } = runFormRoute.useSearch();
    return <RunFormPage name={name} version={version} />;
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

const routeTree = rootRoute.addChildren([
  indexRoute,
  shellRoute.addChildren([
    signalsRoute,
    strategiesRoute,
    runFormRoute,
    runsRoute,
    runRoute,
    setRoute,
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
