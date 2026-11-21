import type { ReactNode } from 'react';

/**
 * The report of a run as text with structure. The report is written by the bench and holds exactly these constructs:
 * headings, a blockquote, paragraphs, pipe tables, and `code` and **bold** inside the lines. A library for markdown
 * would bring far more than that, so the renderer is this small one; it makes React elements only, never HTML.
 */
export function Markdown({ text }: { readonly text: string }) {
  return <div className="flex flex-col gap-3">{blocks(text.split('\n'))}</div>;
}

const heading = /^(#{1,6})\s+(.*)$/;
const separator = /^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$/;

function blocks(lines: readonly string[]): ReactNode[] {
  const out: ReactNode[] = [];
  let at = 0;
  while (at < lines.length) {
    const line = lines[at]!;
    const key = out.length;
    if (line.trim() === '') {
      at++;
    } else if (heading.test(line)) {
      const [, marks, title] = heading.exec(line)!;
      out.push(
        marks!.length === 1 ? (
          <h2 key={key} className="text-base font-semibold">
            {inline(title!)}
          </h2>
        ) : (
          <h3 key={key} className="font-semibold">
            {inline(title!)}
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
          className="flex flex-col gap-2 border-l-2 border-amber-500 pl-3 text-zinc-300"
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

      out.push(
        <div key={key} className="overflow-x-auto">
          <table className="border-collapse text-xs">
            <thead>
              <tr>
                {head.map((cell, index) => (
                  <th key={index} className="border border-zinc-700 px-2 py-1 text-left">
                    {inline(cell)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, rowIndex) => (
                <tr key={rowIndex}>
                  {row.map((cell, index) => (
                    <td key={index} className="border border-zinc-800 px-2 py-1">
                      {inline(cell)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>,
      );
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

      out.push(<p key={key}>{inline(paragraph.join(' '))}</p>);
    }
  }

  return out;
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
        <code key={index} className="font-mono text-zinc-300">
          {part.slice(1, -1)}
        </code>
      ) : part.startsWith('**') ? (
        <strong key={index}>{part.slice(2, -2)}</strong>
      ) : (
        part
      ),
    );
}
