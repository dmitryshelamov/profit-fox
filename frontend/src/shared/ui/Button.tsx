import type { ButtonHTMLAttributes } from 'react';

const looks = {
  primary:
    'bg-accent font-bold text-on-accent hover:brightness-110 disabled:bg-[#3a3226] disabled:text-[#8a7a62]',
  ghost: 'border border-line text-text hover:bg-panel-2 disabled:text-faint',
  plain: 'text-text hover:bg-panel-2 disabled:text-faint',
  danger: 'text-err hover:bg-err-bg',
} as const;

export function Button({
  look = 'ghost',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { look?: keyof typeof looks }) {
  return (
    <button
      type="button"
      className={`inline-flex h-8 items-center gap-1.5 rounded-md px-3 text-sm font-semibold whitespace-nowrap disabled:cursor-not-allowed ${looks[look]} ${className}`}
      {...props}
    />
  );
}
