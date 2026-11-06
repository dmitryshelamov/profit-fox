import { render, screen } from '@testing-library/react';

import { ideaOf } from '@/test/fixtures';

import { ExpectancyCell, type Idea, ProbabilityCell, RiskRewardCell } from './IdeaCells';

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

const withBucket = (
  overrides: Partial<NonNullable<Idea['bucket']>>,
  breakEven: string | null = '0.3517',
) => ideaOf({ breakEven, bucket: { ...ideaOf().bucket!, ...overrides } });

describe('the probability cell', () => {
  // Scenarios "Безубыток под вероятностью" and "Доли по наведению".
  it('shows the probability with N, the break-even under it and the shares on hover', () => {
    render(
      <ProbabilityCell
        idea={withBucket({
          cases: 400,
          targets: 180,
          stops: 200,
          expired: 20,
          probability: '0.45',
        })}
      />,
    );

    expect(screen.getByText('45 %')).toBeInTheDocument();
    expect(screen.getByText('нужно ≥ 36 %')).toBeInTheDocument();
    expect(screen.getByTitle('Цель 45 % · Стоп 50 % · Истекла 5 %')).toBeInTheDocument();
  });

  // Scenario "Мало данных": the break-even of the idea is shown all the same.
  it('shows the break-even under «мало данных»', () => {
    render(
      <ProbabilityCell
        idea={withBucket({ cases: 12, enoughData: false, probability: null, badge: null })}
      />,
    );

    expect(screen.getByText('мало данных')).toBeInTheDocument();
    expect(screen.getByText('нужно ≥ 36 %')).toBeInTheDocument();
  });

  // Scenario "Цель не окупает комиссии".
  it('says the target does not cover the fees for an idea without a bucket', () => {
    render(<ProbabilityCell idea={ideaOf({ breakEven: null, bucket: null })} />);

    expect(screen.getByText('не окупает комиссии')).toBeInTheDocument();
    expect(screen.queryByTitle(/Цель/)).not.toBeInTheDocument();
  });
});

describe('the expectancy cell', () => {
  it.each([
    ['up', '▲', 'ожидание надёжно выше нуля'],
    ['flat', '≈', 'ожидание не отличается от нуля'],
    ['down', '▼', 'ожидание надёжно ниже нуля'],
  ])('draws the badge %s of the bucket before the expectancy', (badge, symbol, label) => {
    render(<ExpectancyCell idea={withBucket({ badge })} />);

    expect(screen.getByRole('img', { name: label })).toHaveTextContent(symbol);
    expect(screen.getByText('+0.38 R')).toBeInTheDocument();
  });

  it('draws no badge while the bucket has too little data', () => {
    render(
      <ExpectancyCell
        idea={withBucket({ cases: 12, enoughData: false, probability: null, badge: null })}
      />,
    );

    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});
