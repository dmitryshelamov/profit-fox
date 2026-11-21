import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type RouterHistory, RouterProvider } from '@tanstack/react-router';
import { useState } from 'react';

import { createAppRouter } from './router';

/** The constructor with its queries and routes; a test passes a memory history. */
export function App({ history }: { readonly history?: RouterHistory }) {
  const [{ queries, router }] = useState(() => ({
    queries: new QueryClient({
      defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
    }),
    router: createAppRouter(history),
  }));

  return (
    <QueryClientProvider client={queries}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}
