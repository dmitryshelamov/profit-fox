import { featureLabel, featureName } from './features';

describe('the features of ideas', () => {
  it('names each feature in full and by its chip, and a code it does not know as it is', () => {
    expect(featureName('day_drop')).toBe('падение пары за сутки');
    expect(featureName('btc_drop')).toBe('падение BTC за 4 часа');
    expect(featureName('high_funding')).toBe('высокий funding');
    expect(featureLabel('high_funding')).toBe('↑ funding');
    expect(featureName('moon')).toBe('moon');
    expect(featureLabel('moon')).toBe('moon');
  });
});
