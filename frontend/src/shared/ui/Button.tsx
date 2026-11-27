import type { ButtonHTMLAttributes } from 'react';

/** The four looks of a button: the one main action of a screen, an outlined one, a text one and a dangerous one. */
export type ButtonVariant = 'primary' | 'ghost' | 'plain' | 'danger';

const looks: Record<ButtonVariant, string> = {
  primary: 'bg-accent text-on-accent enabled:hover:brightness-110',
  ghost: 'border border-line bg-panel text-text enabled:hover:bg-panel-2',
  plain: 'text-text enabled:hover:bg-panel-2',
  danger: 'text-err enabled:hover:bg-err-bg',
};

const sizes = { md: 'h-8 px-3 text-[13px]', sm: 'h-7 px-2 text-xs' };

/** The classes of a button of a look, for a link that looks like one. */
export function buttonLook(variant: ButtonVariant = 'ghost', size: keyof typeof sizes = 'md') {
  return `inline-flex shrink-0 cursor-pointer items-center justify-center gap-1.5 rounded-md font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-45 ${sizes[size]} ${looks[variant]}`;
}

/** A button of the bench, 32 px high. A disabled one is the same look, dimmed. A screen has one `primary` button. */
export function Button({
  variant = 'ghost',
  size = 'md',
  className = '',
  type = 'button',
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  readonly variant?: ButtonVariant;
  readonly size?: keyof typeof sizes;
}) {
  return (
    <button
      type={type}
      data-variant={variant}
      className={`${buttonLook(variant, size)} ${className}`}
      {...rest}
    />
  );
}
