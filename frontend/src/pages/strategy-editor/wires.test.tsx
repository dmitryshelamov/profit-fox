import { fireEvent, screen, waitFor } from '@testing-library/react';

import { openEma } from '@/test/openEditor';

const edge = (container: HTMLElement, id: string) =>
  container.querySelector<HTMLElement>(`.react-flow__edge[data-id="${id}"]`);

describe('wires on the canvas', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('selects a clicked wire and removes it with Delete', async () => {
    const { container } = openEma();
    await screen.findByRole('group', { name: 'Свеча' });
    const amount = edge(container, 'params.amount->enter.size')!;

    fireEvent.click(amount);
    await waitFor(() =>
      expect(edge(container, 'params.amount->enter.size')).toHaveClass('selected'),
    );
    fireEvent.keyDown(document.body, { key: 'Delete' });

    await waitFor(() => expect(edge(container, 'params.amount->enter.size')).toBeNull());
    expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(8);
    expect(screen.getAllByRole('group').map((group) => group.getAttribute('aria-label'))).toContain(
      'Рыночный ордер',
    );
  });
});
