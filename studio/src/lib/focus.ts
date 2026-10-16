// Keeping keyboard focus inside a panel while it is open.

/** What can take focus inside `root`, in order. */
export function focusables(root: HTMLElement): HTMLElement[] {
  // Hidden by an ancestor rather than by measuring, which a test document
  // without layout cannot do.
  return Array.from(root.querySelectorAll<HTMLElement>("button, [href], input, textarea, select, [tabindex='0']")).filter(
    (el) => !(el as HTMLButtonElement).disabled && !el.closest('[hidden]'),
  );
}

/** Wraps Tab and Shift+Tab inside `root`. Returns true when it handled the key. */
export function trapTab(event: KeyboardEvent, root: HTMLElement): boolean {
  if (event.key !== 'Tab') {
    return false;
  }

  const list = focusables(root);

  if (!list.length) {
    return false;
  }

  const first = list[0];
  const last = list[list.length - 1];

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();

    return true;
  }

  if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();

    return true;
  }

  return false;
}
