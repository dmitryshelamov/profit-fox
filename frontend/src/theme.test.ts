/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

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
  'pin-order': '#4FC3D9',
  'pin-position': '#E879B9',
  'group-graph': '#4A5060',
  'group-data': '#2E6E93',
  'group-logic': '#5B49C9',
  'group-position': '#2F8F6B',
  'group-signal': '#B0591A',
  'group-math': '#8C6B2E',
};

describe('the theme', () => {
  it.each(Object.entries(tokens))('has the token %s as %s', (token, color) => {
    expect(theme.toLowerCase()).toContain(`--color-${token}: ${color.toLowerCase()};`);
  });

  it('switches the palette of Tailwind off, so only the tokens give a color', () => {
    expect(theme).toContain('--color-*: initial;');
    expect(theme.match(/--color-[a-z0-9-]+:/g)).toHaveLength(Object.keys(tokens).length);
    expect(Object.keys(tokens)).toHaveLength(33);
  });

  // Change add-position-node, task 8.2: no color outside the theme. The table of the chart, which draws on a canvas and
  // takes colors as values, repeats tokens of the theme; any other hexadecimal color is an error.
  it('has no hexadecimal color outside the theme but the tokens the chart repeats', () => {
    const source = join(process.cwd(), 'src');
    const files = readdirSync(source, { recursive: true, encoding: 'utf8' }).filter(
      (file) =>
        /\.(tsx?|css)$/.test(file) &&
        !/\.test\.tsx?$/.test(file) &&
        file !== 'index.css' &&
        !file.includes('generated'),
    );
    const known = new Set(Object.values(tokens).map((color) => color.toLowerCase()));
    const foreign = files.flatMap((file) =>
      [...readFileSync(join(source, file), 'utf8').matchAll(/#[0-9a-fA-F]{3,8}\b/g)]
        .map((match) => match[0].toLowerCase())
        .filter((color) => !known.has(color))
        .map((color) => `${file}: ${color}`),
    );

    expect(files.length).toBeGreaterThan(50);
    expect(foreign).toEqual([]);
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
