import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';

import { App } from '@/app/App';

/** The whole constructor at a path, with real routes and queries against the fake API. */
export function renderApp(path: string) {
  const history = createMemoryHistory({ initialEntries: [path] });
  return { history, ...render(<App history={history} />) };
}
