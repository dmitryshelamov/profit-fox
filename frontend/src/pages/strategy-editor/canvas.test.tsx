import { screen } from '@testing-library/react';

import catalog from '@/test/catalog.fixture.json';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { emaSummary, savedStrategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

import ema from '../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';

describe('strategy canvas', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenario "Эталонные стратегии открываются полностью" for EMA; the end-to-end test opens all four.
  it('shows every node and wire of EMA, saved nodes at their positions', async () => {
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, ema)),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    const { container } = renderApp(`/strategies/${emaSummary.id}`);

    for (const title of [
      'Параметры',
      'Свеча',
      'Позиция',
      'Пересечение EMA',
      'НЕ',
      'И',
      'Рыночный ордер',
      'Закрыть позицию',
    ]) {
      expect(await screen.findByRole('group', { name: title })).toBeInTheDocument();
    }
    expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(9);
    expect(
      container.querySelector<HTMLElement>('.react-flow__node[data-id="cross"]')!.style.transform,
    ).toBe('translate(240px,0px)');
  });

  it('says so when there is no such strategy', async () => {
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => problem(404, 'Not Found'),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    renderApp(`/strategies/${emaSummary.id}`);

    expect(await screen.findByText('Стратегия не найдена.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'К стратегиям' })).toBeInTheDocument();
  });
});
