import { render, screen } from '@testing-library/react';

import { DraftQuestion } from './DraftQuestion';

// Change fix-pair-chart-and-local-time, task 3.2: the moment of a draft in the zone of the browser (Europe/Moscow in the
// tests), without «UTC».
describe('the question about a draft', () => {
  it('says when the draft was saved in the zone of the browser', () => {
    render(
      <DraftQuestion
        savedAt="2026-11-19T00:00:00Z"
        nodes={2}
        wires={1}
        onRestore={() => undefined}
        onDiscard={() => undefined}
      />,
    );

    const text = screen.getByText(/Черновик от/);
    expect(text).toHaveTextContent('Черновик от 2026-11-19 03:00 отличается от файла');
    expect(text).not.toHaveTextContent('UTC');
  });
});
