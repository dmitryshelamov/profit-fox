import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { NodeEditorPage } from '@/pages/node-editor/NodeEditorPage';
import { NodesPage } from '@/pages/nodes/NodesPage';
import { StrategiesPage } from '@/pages/strategies/StrategiesPage';
import {
  NewStrategyEditorPage,
  StrategyEditorPage,
} from '@/pages/strategy-editor/StrategyEditorPage';

import { AppShell } from './AppShell';
import { NotFoundPage } from './NotFoundPage';

export interface RouterContext {
  readonly queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: Outlet,
  notFoundComponent: () => (
    <AppShell>
      <NotFoundPage />
    </AppShell>
  ),
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/strategies' });
  },
});

// Lists live in the application shell; editors take the whole screen.
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'shell',
  component: () => (
    <AppShell>
      <Outlet />
    </AppShell>
  ),
});

const strategiesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/strategies',
  component: StrategiesPage,
});

const nodesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/nodes',
  component: NodesPage,
});

const newStrategyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/strategies/new/$draftId',
  component: function NewStrategyRoute() {
    const { draftId } = newStrategyRoute.useParams();

    return <NewStrategyEditorPage draftId={draftId} />;
  },
});

const strategyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/strategies/$id',
  component: function StrategyRoute() {
    const { id } = strategyRoute.useParams();

    return <StrategyEditorPage id={id} />;
  },
});

const nodeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/nodes/$id',
  component: function NodeRoute() {
    const { id } = nodeRoute.useParams();

    return <NodeEditorPage id={id} />;
  },
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  shellRoute.addChildren([strategiesRoute, nodesRoute]),
  newStrategyRoute,
  strategyRoute,
  nodeRoute,
]);

export function createAppRouter(queryClient: QueryClient, history?: RouterHistory) {
  return createRouter({ routeTree, context: { queryClient }, history });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
