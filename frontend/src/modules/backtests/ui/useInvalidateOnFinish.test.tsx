import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';

import { useInvalidateOnFinish } from './useInvalidateOnFinish';

function setup() {
  const queryClient = new QueryClient();
  queryClient.setQueryData(['bots'], []);
  queryClient.setQueryData(['bots', 'bot-1'], {});
  queryClient.setQueryData(['bots', 'bot-1', 'backtests'], []);
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  const stale = (...key: string[]) => queryClient.getQueryState(key)!.isInvalidated;

  return { wrapper, stale };
}

describe('invalidation when a backtest finishes', () => {
  it('marks the bots list and the backtests of the bot stale, and nothing else of the bot', () => {
    const { wrapper, stale } = setup();
    const { rerender } = renderHook(({ statuses }) => useInvalidateOnFinish(statuses, 'bot-1'), {
      wrapper,
      initialProps: { statuses: { first: 'Running' } as Record<string, string> },
    });

    rerender({ statuses: { first: 'Succeeded' } });

    expect(stale('bots')).toBe(true);
    expect(stale('bots', 'bot-1', 'backtests')).toBe(true);
    expect(stale('bots', 'bot-1')).toBe(false);
  });

  it('does nothing for a backtest that is finished when first seen', () => {
    const { wrapper, stale } = setup();
    const { rerender } = renderHook(({ statuses }) => useInvalidateOnFinish(statuses, 'bot-1'), {
      wrapper,
      initialProps: { statuses: { first: 'Succeeded' } as Record<string, string> },
    });

    expect(stale('bots')).toBe(false);

    rerender({ statuses: { first: 'Succeeded', second: 'Failed' } });

    expect(stale('bots')).toBe(false);
    expect(stale('bots', 'bot-1', 'backtests')).toBe(false);
  });

  it('does nothing while the backtests are unfinished or not loaded', () => {
    const { wrapper, stale } = setup();
    const { rerender } = renderHook(({ statuses }) => useInvalidateOnFinish(statuses, 'bot-1'), {
      wrapper,
      initialProps: { statuses: undefined as Record<string, string> | undefined },
    });

    rerender({ statuses: { first: 'Pending' } });
    rerender({ statuses: { first: 'Running' } });

    expect(stale('bots')).toBe(false);
  });

  it('does nothing while the bot of the backtests is not known', () => {
    const { wrapper, stale } = setup();
    const { rerender } = renderHook(({ statuses }) => useInvalidateOnFinish(statuses, undefined), {
      wrapper,
      initialProps: { statuses: { first: 'Running' } as Record<string, string> },
    });

    rerender({ statuses: { first: 'Succeeded' } });

    expect(stale('bots')).toBe(false);
  });
});
