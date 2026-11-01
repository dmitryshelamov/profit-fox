import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { draftKey, drafts } from '@/modules/graph-editor';
import { json } from '@/test/fakeApi';
import { emaSummary, savedStrategy } from '@/test/fixtures';
import { emaDefinition, openEma } from '@/test/openEditor';

const save = () => screen.getByRole('button', { name: 'Сохранить' });

const xOf = (transform: string) => Number(/translate\((-?[\d.]+)px/.exec(transform)![1]);

function placesOf(container: HTMLElement): Record<string, string> {
  return Object.fromEntries(
    [...container.querySelectorAll<HTMLElement>('.react-flow__node')].map((node) => [
      node.dataset.id,
      node.style.transform,
    ]),
  );
}

describe('layout of the strategy graph', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // Scenario "Открытие ничего не меняет".
  it('changes nothing when a strategy without coordinates is opened', async () => {
    const { requests } = openEma();

    await screen.findByRole('group', { name: 'Свеча' });

    expect(save()).toBeDisabled();
    expect(drafts.read(draftKey({ strategy: emaSummary.id }))).toBeUndefined();
    expect(requests.filter((request) => request.method !== 'GET')).toHaveLength(0);
  });

  // Scenario "Добавление узла не двигает остальные".
  it('keeps every node in its place when a node is added and saves the places of all nodes', async () => {
    const { container, requests } = openEma({
      [`PUT /api/strategies/${emaSummary.id}`]: () => new Response(null, { status: 204 }),
    });
    await screen.findByRole('group', { name: 'Свеча' });
    const before = placesOf(container);

    await userEvent.click(screen.getByRole('button', { name: 'Добавить узел' }));
    await userEvent.click(screen.getByRole('button', { name: 'Держать лимитку' }));
    await screen.findByRole('group', { name: 'Держать лимитку' });

    expect(placesOf(container)).toMatchObject(before);
    await waitFor(() => expect(save()).toBeEnabled());
    await userEvent.click(save());
    await waitFor(() => expect(requests.some((request) => request.method === 'PUT')).toBe(true));
    const body = await requests.find((request) => request.method === 'PUT')!.json();
    expect(Object.keys(body.definition.layout).sort()).toEqual(
      [...emaDefinition.nodes.map((node) => node.id), 'hold-limit1'].sort(),
    );
  });

  // Scenario "Выстроить и отменить".
  it('lays the graph out by stages and puts the nodes back on the saved places when the edits are dropped', async () => {
    // Saved one under another, as if the owner had dragged them so.
    const moved = {
      ...emaDefinition,
      layout: Object.fromEntries(
        emaDefinition.nodes.map((node, index) => [node.id, { x: 0, y: index * 300 }]),
      ),
    };
    const { container } = openEma({
      [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, moved)),
    });
    await screen.findByRole('group', { name: 'Свеча' });
    const saved = placesOf(container);

    await userEvent.click(screen.getByRole('button', { name: 'Выстроить' }));

    const staged = placesOf(container);
    for (const wire of emaDefinition.wires) {
      expect(xOf(staged[wire.from.split('.')[0]!]!), `${wire.from} -> ${wire.to}`).toBeLessThan(
        xOf(staged[wire.to.split('.')[0]!]!),
      );
    }
    await waitFor(() => expect(save()).toBeEnabled());

    await userEvent.click(screen.getByRole('button', { name: 'Отменить правки' }));

    await waitFor(() => expect(placesOf(container)).toEqual(saved));
    expect(save()).toBeDisabled();
  });
});
