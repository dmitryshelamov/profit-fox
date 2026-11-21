import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { findNode } from '@/test/editor';
import { bodiesOf, fakeApi, json } from '@/test/fakeApi';
import { h0001 } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';
import { baseRoutes, savedFundingHigh } from '@/test/routes';

describe('the signal of a hypothesis', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Scenario «Копия H0001».
  it('opens read only and copies into a new signal with the same graph', async () => {
    const requests = fakeApi({
      ...baseRoutes(),
      'POST /api/signals': async (request) => json({ ...(await request.json()), layout: {} }, 201),
      'GET /api/signals/funding-high': () => json(savedFundingHigh()),
    });
    const { history } = renderApp('/hypotheses/H0001');

    const high = await findNode('Сравнить', 'high');
    expect(screen.queryByRole('button', { name: 'Палитра узлов' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Сохранить' })).toBeNull();
    expect(within(high).queryByRole('textbox')).toBeNull();
    expect(screen.getByText('только чтение')).toBeInTheDocument();

    await userEvent.type(
      screen.getByRole('textbox', { name: 'Имя нового сигнала' }),
      'funding-high',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Скопировать в сигнал' }));

    await waitFor(() => expect(history.location.pathname).toBe('/signals/funding-high'));
    const [created] = (await bodiesOf(requests, 'POST', '/api/signals')) as {
      name: string;
      graph: unknown;
    }[];
    expect(created!.name).toBe('funding-high');
    expect(created!.graph).toEqual(h0001.signal);
    expect(localStorage.getItem('profit-fox:draft:signal:funding-high')).toBeNull();
  });
});
