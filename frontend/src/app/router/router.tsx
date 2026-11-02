import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';

import { AccountPage } from '@/pages/account/AccountPage';
import { NewAccountPage } from '@/pages/account-new/NewAccountPage';
import { AccountsPage } from '@/pages/accounts/AccountsPage';
import { BacktestPage } from '@/pages/backtest/BacktestPage';
import { BacktestResultPage } from '@/pages/backtest-result/BacktestResultPage';
import { BacktestSetupPage } from '@/pages/backtest-setup/BacktestSetupPage';
import { BotPage } from '@/pages/bot/BotPage';
import { BotEditorPage } from '@/pages/bot-editor/BotEditorPage';
import { BotsPage } from '@/pages/bots/BotsPage';
import { ComparePage } from '@/pages/compare/ComparePage';
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

const botsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots',
  component: BotsPage,
});

const newBotRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots/new',
  validateSearch: (search: Record<string, unknown>) => ({
    strategyId: typeof search.strategyId === 'string' ? search.strategyId : undefined,
  }),
  component: function NewBotRoute() {
    const { strategyId } = newBotRoute.useSearch();

    return <BotEditorPage strategyId={strategyId} />;
  },
});

const editBotRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots/$botId/edit',
  component: function EditBotRoute() {
    const { botId } = editBotRoute.useParams();

    return <BotEditorPage botId={botId} />;
  },
});

const botRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots/$botId',
  component: function BotRoute() {
    const { botId } = botRoute.useParams();

    return <BotPage botId={botId} />;
  },
});

const newBacktestRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/bots/$botId/backtests/new',
  component: function NewBacktestRoute() {
    const { botId } = newBacktestRoute.useParams();

    return <BacktestSetupPage botId={botId} />;
  },
});

const backtestRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/backtests/$backtestId',
  component: function BacktestRoute() {
    const { backtestId } = backtestRoute.useParams();

    return <BacktestPage backtestId={backtestId} />;
  },
});

const resultRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/backtests/$backtestId/results/$combination',
  component: function ResultRoute() {
    const { backtestId, combination } = resultRoute.useParams();

    return <BacktestResultPage backtestId={backtestId} combination={combination} />;
  },
});

const accountsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/accounts',
  component: AccountsPage,
});

const newAccountRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/accounts/new',
  component: NewAccountPage,
});

const accountRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/accounts/$accountId',
  component: function AccountRoute() {
    const { accountId } = accountRoute.useParams();

    return <AccountPage accountId={accountId} />;
  },
});

const compareRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/compare',
  validateSearch: (search: Record<string, unknown>) => ({
    items: typeof search.items === 'string' ? search.items : undefined,
  }),
  component: function CompareRoute() {
    const { items } = compareRoute.useSearch();

    return <ComparePage items={items} />;
  },
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
  shellRoute.addChildren([
    strategiesRoute,
    nodesRoute,
    botsRoute,
    newBotRoute,
    editBotRoute,
    botRoute,
    newBacktestRoute,
    backtestRoute,
    resultRoute,
    compareRoute,
    accountsRoute,
    newAccountRoute,
    accountRoute,
  ]),
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
