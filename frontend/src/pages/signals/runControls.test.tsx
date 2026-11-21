import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { renderApp } from '@/test/renderApp';
import { baseRoutes } from '@/test/routes';

describe('the buttons of a run in the list of hypotheses', () => {
  afterEach(() => vi.unstubAllGlobals());

  async function row() {
    return screen.findByRole('rowgroup', { name: 'Гипотеза H0001' });
  }

  // Scenario «Отложенный без разведки».
  it('disables the holdout with the reason of the API when the exploration has not passed', async () => {
    fakeApi(baseRoutes());
    renderApp('/signals');

    const hypothesis = within(await row());
    expect(hypothesis.getByRole('option', { name: 'отложенный' })).toBeDisabled();
    expect(hypothesis.getByRole('option', { name: 'новые данные' })).toBeDisabled();
    expect(hypothesis.getByRole('option', { name: 'разведка' })).toBeEnabled();
    expect(
      hypothesis.getByText(/отложенный, с вердиктом: the holdout opens after «passed»/),
    ).toBeInTheDocument();
    expect(hypothesis.getByRole('button', { name: 'Прогнать H0001' })).toBeEnabled();
  });

  // Scenario «Постановка из списка».
  it('queues a parity run and opens the section «Прогоны»', async () => {
    const requests = fakeApi({
      ...baseRoutes(),
      'POST /api/runs': () =>
        json(
          {
            id: 'r1',
            hypothesis: 'H0001',
            version: 1,
            period: 'exploration',
            kind: 'parity',
            state: 'queued',
            pairsDone: 0,
            lostLeases: 0,
            cancelRequested: false,
          },
          201,
        ),
      'GET /api/runs': () => json([]),
    });
    const { history } = renderApp('/signals');

    await userEvent.click(within(await row()).getByRole('button', { name: 'Сверка H0001' }));

    expect(await screen.findByRole('heading', { name: 'Прогоны' })).toBeInTheDocument();
    expect(history.location.pathname).toBe('/runs');
    expect(await bodiesOf(requests, 'POST', '/api/runs')).toEqual([
      { hypothesis: 'H0001', version: 1, period: 'exploration', kind: 'parity' },
    ]);
  });

  it('shows the reason of a refusal that comes on queueing and stays on the list', async () => {
    fakeApi({
      ...baseRoutes(),
      'POST /api/runs': () =>
        json({ reason: 'H0001 v1 has a verdict in the exploration', runId: 'r0' }, 409),
    });
    const { history } = renderApp('/signals');

    await userEvent.click(within(await row()).getByRole('button', { name: 'Прогнать H0001' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'H0001 v1 has a verdict in the exploration',
    );
    expect(history.location.pathname).toBe('/signals');
  });
});
