import { render, screen } from '@testing-library/react';

import { IdeaFeatureChips } from './IdeaFeatureChips';

describe('the chips of the features of an idea', () => {
  // Scenario "Два признака": the chips in the order of the features, the full name with its bound on hover.
  it('shows a chip of each feature the idea has with its name and bound on hover', () => {
    render(<IdeaFeatureChips features={['btc_drop', 'day_drop']} />);

    expect(screen.getAllByText(/↓/).map((chip) => chip.textContent)).toEqual(['↓ пара', '↓ BTC']);
    expect(screen.getByText('↓ пара')).toHaveAttribute(
      'title',
      'пара упала за сутки на 5 % и больше',
    );
    expect(screen.getByText('↓ BTC')).toHaveAttribute(
      'title',
      'BTC упал за 4 часа на 1 % и больше',
    );
  });

  it('names a high funding with its bound', () => {
    render(<IdeaFeatureChips features={['high_funding']} />);

    expect(screen.getByText('↑ funding')).toHaveAttribute('title', 'funding 0,05 % и больше');
  });

  // Scenario "Без признаков".
  it('shows nothing for an idea without features', () => {
    const { container } = render(<IdeaFeatureChips features={[]} />);

    expect(container).toBeEmptyDOMElement();
  });
});
