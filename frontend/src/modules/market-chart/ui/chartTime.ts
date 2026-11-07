import type { TickMarkType, Time } from 'lightweight-charts';

import { crosshairTime, tickMarkText } from '../model/chartTime';

/** The options of every chart for its time: UTC seconds in the data, the axis and the crosshair in the local zone. */
export const localTime = {
  localization: { timeFormatter: (time: Time) => crosshairTime(time as number) },
  timeScale: {
    timeVisible: true,
    secondsVisible: false,
    tickMarkFormatter: (time: Time, type: TickMarkType) => tickMarkText(time as number, type),
  },
};
