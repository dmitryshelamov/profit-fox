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

  it('removes a selected node and a selected wire of other nodes with one Delete', async () => {
    const { container } = openEma();
    await screen.findByRole('group', { name: 'Свеча' });

    fireEvent.click(container.querySelector('.react-flow__node[data-id="flat"]')!);
    fireEvent.keyDown(document, { key: 'Control' });
    fireEvent.click(edge(container, 'params.amount->enter.size')!, { ctrlKey: true });
    fireEvent.keyUp(document, { key: 'Control' });
    await waitFor(() =>
      expect(edge(container, 'params.amount->enter.size')).toHaveClass('selected'),
    );
    expect(container.querySelector('.react-flow__node[data-id="flat"]')).toHaveClass('selected');
    fireEvent.keyDown(document.body, { key: 'Delete' });

    await waitFor(() =>
      expect(container.querySelector('.react-flow__node[data-id="flat"]')).toBeNull(),
    );
    expect(edge(container, 'params.amount->enter.size')).toBeNull();
  });
});
