// Change add-ideas-and-market, task 18.5: on a disk that ignores case (macOS), `pairChart.ts` and `PairChart.tsx` in one
// folder are one module for an import without extension — the app did not open and the build failed. No two modules of a
// folder of `src` may have names that differ only by case or by extension.

/** The modules of `src`, as paths from the root of the project. */
const modules = Object.keys(import.meta.glob('/src/**/*.{ts,tsx,js,jsx,json,css}'));

/** The groups of paths of one folder whose names without extension are the same when case is ignored. */
function nameClashes(paths: readonly string[]): string[][] {
  const byName = new Map<string, string[]>();
  for (const path of paths) {
    const slash = path.lastIndexOf('/');
    const file = path.slice(slash + 1);
    const dot = file.lastIndexOf('.');
    const name = `${path.slice(0, slash)}/${dot > 0 ? file.slice(0, dot) : file}`.toLowerCase();
    byName.set(name, [...(byName.get(name) ?? []), path]);
  }

  return [...byName.values()].filter((group) => group.length > 1);
}

describe('the names of the modules of src', () => {
  it('finds the former pair that differed only by case', () => {
    expect(
      nameClashes([
        '/src/pages/market/pairChart.ts',
        '/src/pages/market/PairChart.tsx',
        '/src/pages/market/pairSeries.ts',
      ]),
    ).toEqual([['/src/pages/market/pairChart.ts', '/src/pages/market/PairChart.tsx']]);
  });

  it('takes a test and its module apart', () => {
    expect(nameClashes(['/src/pages/market/pair.test.tsx', '/src/pages/market/Pair.tsx'])).toEqual(
      [],
    );
  });

  it('has no two modules in one folder whose names differ only by case or extension', () => {
    expect(modules.length).toBeGreaterThan(50);
    expect(nameClashes(modules)).toEqual([]);
  });
});
