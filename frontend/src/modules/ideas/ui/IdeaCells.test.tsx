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

const withNumbers = (
  numbers: Partial<Idea['numbers']>,
  bucket: Partial<NonNullable<Idea['bucket']>> = {},
) =>
  ideaOf({
    numbers: { ...ideaOf().numbers, ...numbers },
    bucket: { ...ideaOf().bucket!, ...bucket },
  });

describe('the probability cell', () => {
  // Scenarios "Безубыток под вероятностью" and "Доли по наведению": the probability of the idea with N of its bucket.
  it('shows the probability of the idea with N, the break-even under it and the shares on hover', () => {
    render(
      <ProbabilityCell
        idea={withNumbers(
          { probability: '0.45' },
          { cases: 400, targets: 180, stops: 200, expired: 20, probability: '0.5' },
        )}
      />,
    );

    expect(screen.getByText('45 %')).toHaveTextContent('45 %· 400');
    expect(screen.getByText('нужно ≥ 36 %')).toBeInTheDocument();
    expect(screen.getByTitle('Цель 45 % · Стоп 50 % · Истекла 5 %')).toBeInTheDocument();
  });

  // Scenario "Множитель «Уровней» по наведению".
  it('shows the probability of «Уровни» and the multiplier of its band on hover', () => {
    render(
      <ProbabilityCell
        idea={withNumbers(
          { probability: '0.1485', multiplier: '0.675', band: 'from_3_to_6' },
          { cases: 400, targets: 180, stops: 200, expired: 20, probability: '0.22' },
        )}
      />,
    );

    expect(
      screen.getByTitle('корзина 22 % × 0.68 по R:R 3–6 · Цель 45 % · Стоп 50 % · Истекла 5 %'),
    ).toHaveTextContent('15 %');
  });

  // Scenario "Мало данных": the break-even of the idea is shown all the same.
  it('shows the break-even under «мало данных»', () => {
    render(
      <ProbabilityCell
        idea={withNumbers(
          { probability: null },
          { cases: 12, enoughData: false, probability: null },
        )}
      />,
    );

    expect(screen.getByText('мало данных')).toBeInTheDocument();
    expect(screen.getByText('нужно ≥ 36 %')).toBeInTheDocument();
  });

  // Scenario "Цель не окупает комиссии".
  it('says the target does not cover the fees for an idea without a bucket', () => {
    render(
      <ProbabilityCell
        idea={ideaOf({
          breakEven: null,
          bucket: null,
          numbers: { ...ideaOf().numbers, probability: null },
        })}
      />,
    );

    expect(screen.getByText('не окупает комиссии')).toBeInTheDocument();
    expect(screen.queryByTitle(/Цель/)).not.toBeInTheDocument();
  });
});

describe('the expectancy cell', () => {
  it.each([
    ['up', '▲', 'ожидание надёжно выше нуля'],
    ['flat', '≈', 'ожидание не отличается от нуля'],
    ['down', '▼', 'ожидание надёжно ниже нуля'],
  ])('draws the badge %s of the idea before its expectancy', (badge, symbol, label) => {
    render(<ExpectancyCell idea={withNumbers({ badge, expectancy: '-0.72' }, { badge: 'up' })} />);

    expect(screen.getByRole('img', { name: label })).toHaveTextContent(symbol);
    expect(screen.getByText('-0.72 R')).toBeInTheDocument();
  });

  // Scenario "Две идеи одной корзины": the expectancy of each idea and, under it, on 100 USDT of the amount.
  it('shows the expectancy of the idea with the money on 100 USDT under it', () => {
    render(
      <>
        <ExpectancyCell
          idea={withNumbers({ expectancy: '-0.72', expectancyPerUsdt: '-0.00216' })}
        />
        <ExpectancyCell
          idea={withNumbers({ expectancy: '-0.062', expectancyPerUsdt: '-0.0031' })}
        />
      </>,
    );

    expect(screen.getByText('-0.72 R')).toBeInTheDocument();
    expect(screen.getByText('на 100 USDT: -0.22')).toBeInTheDocument();
    expect(screen.getByText('-0.06 R')).toBeInTheDocument();
    expect(screen.getByText('на 100 USDT: -0.31')).toBeInTheDocument();
  });

  // Scenarios "Из чего сложено ожидание" and "Собственные числа по наведению".
  it('tells what the expectancy is made of and the own numbers of the bucket on hover', () => {
    render(
      <ExpectancyCell
        idea={withNumbers(
          { expectancy: '-0.72', costs: '0.7' },
          {
            grossExpectancy: '-0.02',
            cases: 35,
            ownProbability: '0.4',
            ownExpectancy: '0.3',
            parentWeight: '0.8',
          },
        )}
      />,
    );

    expect(screen.getByText('-0.72 R').closest('[title]')).toHaveAttribute(
      'title',
      'без издержек -0.02 R − издержки 0.70 R\nв самой корзине: 35 случаев · 40 % · +0.30 R · по родителю 80 %',
    );
    expect(screen.queryByText('мало своих данных')).not.toBeInTheDocument();
  });

  // Scenario "Мало своих данных": the probability, the badge and the expectancy of the idea, and the line under the expectancy; N 12.
  it('says «мало своих данных» under the expectancy of a bucket of few own cases', () => {
    const idea = withNumbers(
      { probability: '0.36', badge: 'flat', expectancy: '-0.15' },
      {
        cases: 12,
        ownProbability: null,
        ownExpectancy: '0.5',
        parentWeight: '0.92',
        fewOwnCases: true,
      },
    );

    render(
      <>
        <ProbabilityCell idea={idea} />
        <ExpectancyCell idea={idea} />
      </>,
    );

    expect(screen.getByText('36 %')).toHaveTextContent('36 %· 12');
    expect(screen.getByRole('img', { name: 'ожидание не отличается от нуля' })).toBeInTheDocument();
    expect(screen.getByText('-0.15 R')).toBeInTheDocument();
    expect(screen.getByText('мало своих данных')).toBeInTheDocument();
  });

  // Scenario "Идея без ожидания": a dash, without a badge and without the line on 100 USDT.
  it('shows a dash without a badge and without the money for an idea without an expectancy', () => {
    render(
      <ExpectancyCell
        idea={withNumbers(
          {
            expectancy: null,
            expectancyLow: null,
            expectancyHigh: null,
            badge: null,
            expectancyPerUsdt: null,
          },
          { badge: 'up' },
        )}
      />,
    );

    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText(/на 100 USDT/)).not.toBeInTheDocument();
  });
});
