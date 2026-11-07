/** A feature of the market an idea fired with: its code in the API, its chip, its full name and its bound. */
export interface IdeaFeatureLabel {
  readonly code: string;
  readonly chip: string;
  readonly name: string;
  readonly hint: string;
}

/** The features in the order the chips, the conditions of an auto and the breakdown of the calibration show them. */
export const ideaFeatures: readonly IdeaFeatureLabel[] = [
  {
    code: 'day_drop',
    chip: '↓ пара',
    name: 'падение пары за сутки',
    hint: 'пара упала за сутки на 5 % и больше',
  },
  {
    code: 'btc_drop',
    chip: '↓ BTC',
    name: 'падение BTC за 4 часа',
    hint: 'BTC упал за 4 часа на 1 % и больше',
  },
  {
    code: 'high_funding',
    chip: '↑ funding',
    name: 'высокий funding',
    hint: 'funding 0,05 % и больше',
  },
];

const byCode = (code: string) => ideaFeatures.find((feature) => feature.code === code);

/** «падение пары за сутки»; a code the interface does not know is shown as it is. */
export const featureName = (code: string) => byCode(code)?.name ?? code;

/** «↓ пара»; a code the interface does not know is shown as it is. */
export const featureLabel = (code: string) => byCode(code)?.chip ?? code;
