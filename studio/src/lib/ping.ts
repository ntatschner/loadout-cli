// One expanding ring where something just happened. Skipped under reduced or
// no motion; the EventMark beside it stays either way.
import type { SignalFamily } from './signals';
import { fullMotion } from './studio';

export function ping(container: HTMLElement, x: number, y: number, family: SignalFamily = 'moving'): void {
  if (!fullMotion()) {
    return;
  }

  const ring = document.createElement('span');
  ring.className = 'ls-ping';
  ring.setAttribute('aria-hidden', 'true');

  // Through the style object rather than a style attribute, which Studio's
  // content security policy does not allow.
  ring.style.left = `${x}px`;
  ring.style.top = `${y}px`;
  ring.style.setProperty('--c', `var(--${family})`);

  container.appendChild(ring);
  setTimeout(() => ring.remove(), 1500);
}
