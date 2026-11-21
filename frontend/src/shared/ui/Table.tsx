import type { HTMLAttributes, ReactNode, TdHTMLAttributes, ThHTMLAttributes } from 'react';

/** A table in a card: a head of column names, rows with a divider, the row under the pointer lit. */
export function Table({
  label,
  className = '',
  children,
}: {
  readonly label?: string;
  readonly className?: string;
  readonly children: ReactNode;
}) {
  return (
    <div className={`overflow-hidden rounded-[10px] border border-line bg-panel ${className}`}>
      <table aria-label={label} className="w-full border-collapse text-left text-[13px]">
        {children}
      </table>
    </div>
  );
}

/** The name of a column: 11 px, semibold, capitals. */
export function Th({ className = '', ...rest }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      className={`h-10 px-5 text-[11px] font-semibold tracking-wide whitespace-nowrap text-muted uppercase ${className}`}
      {...rest}
    />
  );
}

/** A row of the body; `className` gives its height. */
export function Tr({ className = '', ...rest }: HTMLAttributes<HTMLTableRowElement>) {
  return <tr className={`border-t border-line hover:bg-panel-2 ${className}`} {...rest} />;
}

export function Td({ className = '', ...rest }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={`px-5 ${className}`} {...rest} />;
}
