import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';

import { AppProviders } from '@/app/providers/AppProviders';

/** Renders the whole application at a path, with real routing and queries against the fake API. */
export function renderApp(path: string) {
  const history = createMemoryHistory({ initialEntries: [path] });

  return { history, ...render(<AppProviders history={history} />) };
}
