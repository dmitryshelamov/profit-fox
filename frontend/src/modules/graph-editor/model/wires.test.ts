import catalogJson from '@/test/catalog.fixture.json';

import ema from '../../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import type { CatalogNode, StrategyDef } from './types';
import { canConnect } from './wires';

const catalog = catalogJson as CatalogNode[];
const definition = ema as unknown as StrategyDef;
const context = { parameters: definition.parameters };

describe('wire compatibility', () => {
  // Scenario "Несовместимые типы".
  it('rejects a wire between pins of different types', () => {
    expect(canConnect(definition, catalog, context, 'cross.up', 'enter.size')).toBe(false);
  });

  it('accepts a wire from an output to an input of the same type', () => {
    expect(canConnect(definition, catalog, context, 'candle.open', 'enter.size')).toBe(true);
  });

  it('rejects pins a node does not have and wires from an input', () => {
    expect(canConnect(definition, catalog, context, 'candle.nope', 'enter.size')).toBe(false);
    expect(canConnect(definition, catalog, context, 'enter.size', 'cross.source')).toBe(false);
  });

  it('follows the parameters of the strategy', () => {
    expect(canConnect(definition, catalog, context, 'params.amount', 'enter.size')).toBe(true);
    expect(canConnect(definition, catalog, { parameters: [] }, 'params.amount', 'enter.size')).toBe(
      false,
    );
  });
});
