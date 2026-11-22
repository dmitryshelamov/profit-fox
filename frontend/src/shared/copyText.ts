/**
 * Copies a text and says whether it did. A page served over HTTPS or from localhost has `navigator.clipboard`; the
 * bench is served over plain HTTP from an address of the network, where it is absent, so the text then goes through a
 * field off the screen and the old command of the document.
 */
export async function copyText(text: string): Promise<boolean> {
  const clipboard = navigator.clipboard as Clipboard | undefined;
  if (clipboard !== undefined) {
    try {
      await clipboard.writeText(text);
      return true;
    } catch {
      // Refused, for one by the permissions of the page: the old way is tried.
    }
  }

  const focused = document.activeElement;
  const field = document.createElement('textarea');
  field.value = text;
  field.readOnly = true;
  field.style.position = 'fixed';
  field.style.opacity = '0';
  document.body.append(field);
  field.focus();
  field.select();
  try {
    // Deprecated, and the only way to copy on a page without `navigator.clipboard`.
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    field.remove();
    if (focused instanceof HTMLElement) {
      focused.focus();
    }
  }
}
