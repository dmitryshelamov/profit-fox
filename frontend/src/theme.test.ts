/// <reference types="node" />
import { readFileSync } from 'node:fs';

// Vitest gives an empty text for an imported style sheet, so the two files are read from the disk.
const read = (file: string) => readFileSync(new URL(file, import.meta.url), 'utf8');
const fonts = read('./fonts.css');
const theme = read('./index.css');

/** The tokens of the theme (change redesign-bench-ui, design.md, decision 2), each with its color. */
const tokens: Record<string, string> = {
  app: '#14161B',
  canvas: '#101216',
  panel: '#1B1E25',
  'panel-2': '#232731',
  line: '#2C313B',
  text: '#E7E9EE',
  muted: '#8C94A3',
  faint: '#5D6573',
  accent: '#F28C28',
  'on-accent': '#1A1206',
  run: '#7AA7FF',
  'run-bg': '#1C2A47',
  ok: '#3FB37F',
  'ok-bg': '#16332A',
  warn: '#E0A43A',
  'warn-bg': '#3A2E16',
  err: '#E5484D',
  'err-bg': '#3A1D20',
  chip: '#2D323D',
  node: '#262A33',
  'node-line': '#3A404C',
  'pin-number': '#8BD450',
  'pin-boolean': '#E5534B',
  'pin-duration': '#A78BFA',
  'pin-event': '#F5B84A',
  'group-graph': '#4A5060',
  'group-data': '#2E6E93',
  'group-logic': '#5B49C9',
  'group-position': '#2F8F6B',
  'group-signal': '#B0591A',
};

describe('the theme', () => {
  it.each(Object.entries(tokens))('has the token %s as %s', (token, color) => {
    expect(theme.toLowerCase()).toContain(`--color-${token}: ${color.toLowerCase()};`);
  });

  it('emits every token, used or not, and sets the two font families', () => {
    expect(theme).toContain('@theme static');
    expect(theme).toMatch(/--font-sans:\s*'Inter Variable'/);
    expect(theme).toMatch(/--font-mono:\s*'JetBrains Mono Variable'/);
    expect(theme).toContain("--font-sans--font-variation-settings: 'opsz' 32;");
  });

  // The bench serves its own fonts: only the latin and cyrillic sets, from the packages, no outside address.
  it('takes the fonts from the packages in the latin and cyrillic sets only', () => {
    const files = [...fonts.matchAll(/url\(([^)]+)\)/g)].map((match) => match[1]);
    expect(files.sort()).toEqual([
      '@fontsource-variable/inter/files/inter-cyrillic-opsz-normal.woff2',
      '@fontsource-variable/inter/files/inter-latin-opsz-normal.woff2',
      '@fontsource-variable/jetbrains-mono/files/jetbrains-mono-cyrillic-wght-normal.woff2',
      '@fontsource-variable/jetbrains-mono/files/jetbrains-mono-latin-wght-normal.woff2',
    ]);
    expect(fonts).not.toMatch(/https?:/);
  });
});
