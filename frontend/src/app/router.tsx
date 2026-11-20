import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { HypothesisPage } from '@/pages/hypotheses/HypothesisPage';
import { RunPage } from '@/pages/runs/RunPage';
import { RunsPage } from '@/pages/runs/RunsPage';
import { NewSignalPage, SavedSignalPage } from '@/pages/signals/SignalEditorPage';
import { SignalListPage } from '@/pages/signals/SignalListPage';

const rootRoute = createRootRoute({
  component: Outlet,
  notFoundComponent: () => <p className="p-6 text-sm text-zinc-300">Такой страницы нет.</p>,
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/signals' });
  },
});

const signalsRoute = createRoute({
  getParentRoute: () => rootRoute,
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

const hypothesisRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/hypotheses/$id',
  component: function HypothesisRoute() {
    const { id } = hypothesisRoute.useParams();
    return <HypothesisPage id={id} />;
  },
});

const runsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/runs',
  component: RunsPage,
});

const runRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/runs/$id',
  component: function RunRoute() {
    const { id } = runRoute.useParams();
    return <RunPage id={id} />;
  },
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  signalsRoute,
  newSignalRoute,
  signalRoute,
  hypothesisRoute,
  runsRoute,
  runRoute,
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
