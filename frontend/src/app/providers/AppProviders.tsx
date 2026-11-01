import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type RouterHistory, RouterProvider } from '@tanstack/react-router';
import { useState } from 'react';

import { createAppRouter } from '@/app/router/router';
import { ApiError } from '@/shared/api/client';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
}

export function AppProviders({ history }: { history?: RouterHistory }) {
  const [{ queryClient, router }] = useState(() => {
    const client = createQueryClient();

    return { queryClient: client, router: createAppRouter(client, history) };
  });

  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}
