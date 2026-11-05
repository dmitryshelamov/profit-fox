import { render, screen } from '@testing-library/react';

import { RiskRewardCell } from './IdeaCells';

describe('the R:R cell', () => {
  // Scenario "Близкий стоп": 1.56 after the fees in a green chip, the 2.00 of the prices below it.
  it('shows the reward to risk after the fees in a green chip with the price ratio under it', () => {
    render(<RiskRewardCell netRiskReward="1.5614" riskReward="2" />);

    expect(screen.getByText('1.56')).toHaveClass('bg-ok/15', 'text-ok');
    expect(screen.getByText('без ком. 2.00')).toBeInTheDocument();
  });

  it('keeps the chip green at exactly 1, where the profit equals the loss', () => {
    render(<RiskRewardCell netRiskReward="1" riskReward="1.5" />);

    expect(screen.getByText('1.00')).toHaveClass('text-ok');
  });

  it('turns the chip red under 1', () => {
    render(<RiskRewardCell netRiskReward="0.9549" riskReward="1.5" />);

    expect(screen.getByText('0.95')).toHaveClass('bg-err-bg', 'text-err');
    expect(screen.getByText('без ком. 1.50')).toBeInTheDocument();
  });

  // Scenario "Комиссии больше прибыли": the fees eat the whole reward.
  it('shows a negative value in a red chip', () => {
    render(<RiskRewardCell netRiskReward="-0.1234" riskReward="2" />);

    expect(screen.getByText('-0.12')).toHaveClass('bg-err-bg', 'text-err');
  });
});
