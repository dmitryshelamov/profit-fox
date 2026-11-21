import type { ReactNode } from 'react';

/** A table of the report longer than this many rows scrolls inside its own area (spec bench-run-queue «Длинная таблица отчёта»). */
export const longTableRows = 12;

/**
 * The report of a run as text with structure. The report is written by the bench and holds exactly these constructs:
 * headings, a blockquote, paragraphs, pipe tables, and `code` and **bold** inside the lines. A library for markdown
 * would bring far more than that, so the renderer is this small one; it makes React elements only, never HTML. Each
 * section of the report — what stands under one heading — is a card.
 */
export function Markdown({ text }: { readonly text: string }) {
  return (
    <div className="flex flex-col gap-3.5">
      {sections(text.split('\n')).map((lines, index) => (
        <div
          key={index}
          className="flex flex-col gap-3 rounded-[10px] border border-line bg-panel px-5 py-4"
        >
          {blocks(lines)}
        </div>
      ))}
    </div>
  );
}

const heading = /^(#{1,6})\s+(.*)$/;
const separator = /^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$/;
const numeric = /^[-+−]?\d[\d\s.,]*(\s?[%с])?$/;
// A column named by a sign of a formula, as «t» or «Нужный t», keeps its case: «T» is another sign.
const latinSign = /(^|\s)[a-z]$/;

/** The lines of the report by sections: a heading starts a new one. */
function sections(lines: readonly string[]): string[][] {
  const out: string[][] = [];
  for (const line of lines) {
    if (heading.test(line) || out.length === 0) {
      out.push([]);
    }

    out.at(-1)!.push(line);
  }

  return out.filter((section) => section.some((line) => line.trim() !== ''));
}

function blocks(lines: readonly string[]): ReactNode[] {
  const out: ReactNode[] = [];
  let at = 0;
  // The count of rows of the table under a heading stands next to that heading.
  const rowsAfter = (from: number): number | null => {
    let next = from;
    while (next < lines.length && lines[next]!.trim() === '') {
      next++;
    }

    if (!(lines[next]?.startsWith('|') && separator.test(lines[next + 1]?.trim() ?? ''))) {
      return null;
    }

    let count = 0;
    for (let row = next + 2; row < lines.length && lines[row]!.startsWith('|'); row++) {
      count++;
    }

    return count;
  };

  while (at < lines.length) {
    const line = lines[at]!;
    const key = out.length;
    if (line.trim() === '') {
      at++;
    } else if (heading.test(line)) {
      const [, marks, title] = heading.exec(line)!;
      const rows = rowsAfter(at + 1);
      out.push(
        marks!.length === 1 ? (
          <h2 key={key} className="text-base font-bold">
            {inline(title!)}
          </h2>
        ) : (
          <h3 key={key} className="flex items-baseline gap-3 text-sm font-bold">
            {inline(title!)}
            {rows !== null && rows > longTableRows && (
              <span className="text-xs font-normal text-muted">{rowsWords(rows)}</span>
            )}
          </h3>
        ),
      );
      at++;
    } else if (line.startsWith('>')) {
      const quoted: string[] = [];
      while (at < lines.length && lines[at]!.startsWith('>')) {
        quoted.push(lines[at]!.replace(/^>\s?/, ''));
        at++;
      }

      out.push(
        <blockquote
          key={key}
          className="flex flex-col gap-2 rounded-md border-l-2 border-warn bg-panel-2 px-4 py-3 text-xs text-muted [&_strong]:text-warn"
        >
          {blocks(quoted)}
        </blockquote>,
      );
    } else if (line.startsWith('|') && separator.test(lines[at + 1]?.trim() ?? '')) {
      const head = cells(line);
      at += 2;
      const rows: string[][] = [];
      while (at < lines.length && lines[at]!.startsWith('|')) {
        rows.push(cells(lines[at]!));
        at++;
      }

      out.push(<ReportTable key={key} head={head} rows={rows} />);
    } else {
      const paragraph: string[] = [];
      while (
        at < lines.length &&
        lines[at]!.trim() !== '' &&
        !heading.test(lines[at]!) &&
        !lines[at]!.startsWith('>') &&
        !lines[at]!.startsWith('|')
      ) {
        paragraph.push(lines[at]!);
        at++;
      }

      out.push(
        <p key={key} className="leading-relaxed">
          {inline(paragraph.join(' '))}
        </p>,
      );
    }
  }

  return out;
}

/** «54 строки», «21 строка», «5 строк». */
function rowsWords(count: number): string {
  const last = count % 10;
  const teen = count % 100 >= 11 && count % 100 <= 14;
  const word =
    !teen && last === 1 ? 'строка' : !teen && last >= 2 && last <= 4 ? 'строки' : 'строк';
  return `${count} ${word}`;
}

/**
 * A table of the report. A long one — more than 12 rows of the body — is as high as 12 rows and scrolls inside its
 * area, which takes the focus so the keys scroll it, and its head stays in place; a shorter one is shown whole. A
 * column of numbers is set in mono to the right.
 */
function ReportTable({
  head,
  rows,
}: {
  readonly head: readonly string[];
  readonly rows: readonly (readonly string[])[];
}) {
  const long = rows.length > longTableRows;
  const numbers = head.map(
    (_, column) =>
      column > 0 &&
      rows.length > 0 &&
      rows.every((row) => {
        const cell = row[column] ?? '';
        return cell === '' || cell === '—' || numeric.test(cell);
      }),
  );
  // A table of names and values has no column names: its head is not drawn.
  const headless = head.every((cell) => cell === '');
  return (
    <div
      data-scroll={long}
      tabIndex={long ? 0 : undefined}
      className={`-mx-5 overflow-x-auto outline-none ${long ? 'max-h-[432px] overflow-y-auto' : ''}`}
    >
      <table className="w-full border-collapse text-[13px]">
        <thead
          className={`${long ? 'sticky top-0 z-10 bg-panel' : ''} ${headless ? 'sr-only' : ''}`}
        >
          <tr>
            {head.map((cell, index) => (
              <th
                key={index}
                className={`h-9 border-b border-line px-5 text-[11px] font-semibold whitespace-nowrap text-muted ${latinSign.test(cell) ? '' : 'uppercase'} ${numbers[index] ? 'text-right' : 'text-left'}`}
              >
                {inline(cell)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, rowIndex) => (
            <tr key={rowIndex} className="h-8 border-b border-line last:border-b-0">
              {row.map((cell, index) => (
                <td
                  key={index}
                  className={`px-5 ${numbers[index] ? 'text-right font-mono' : ''} ${cell === '0' || cell === '—' ? 'text-faint' : ''}`}
                >
                  {inline(cell)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function cells(row: string): string[] {
  return row
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());
}

function inline(text: string): ReactNode[] {
  return text
    .split(/(`[^`]+`|\*\*[^*]+\*\*)/)
    .filter((part) => part !== '')
    .map((part, index) =>
      part.startsWith('`') ? (
        <code key={index} className="font-mono text-xs break-all text-muted">
          {part.slice(1, -1)}
        </code>
      ) : part.startsWith('**') ? (
        <strong key={index}>{part.slice(2, -2)}</strong>
      ) : (
        part
      ),
    );
}
