// The field map as the page holds it: hidden from assistive technology,
// drawn only once it is wanted, its path data held once and reused, and not
// a single inline style, which Studio's policy would refuse.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/svelte';
import { tick } from 'svelte';
import FieldMap from './FieldMap.svelte';

/** Lets the deferred first draw happen. */
async function drawn() {
  await vi.waitFor(() => {
    if (!document.querySelector('.ls-fieldmap svg')) {
      throw new Error('not drawn yet');
    }
  });
  await tick();
}

describe('FieldMap', () => {
  afterEach(() => vi.useRealTimers());

  it('draws nothing while the plain ground is showing', async () => {
    const { container } = render(FieldMap, { shown: 'plain' });

    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(container.querySelector('.ls-fieldmap')!.getAttribute('aria-hidden')).toBe('true');
    expect(container.querySelector('svg')).toBeNull();
  });

  it('holds its path data once, reuses it everywhere else, and has no inline style', async () => {
    const { container } = render(FieldMap, { shown: 'map' });

    await drawn();

    const map = container.querySelector('.ls-fieldmap')!;
    expect(map.getAttribute('aria-hidden')).toBe('true');
    expect(map.querySelectorAll('path')).toHaveLength(2);
    expect(map.querySelectorAll('defs > path')).toHaveLength(2);
    expect(map.querySelectorAll('path:not([d]), path[d=""]')).toHaveLength(0);
    // Dark edge, light edge and line for each set, then the scan's two.
    expect(map.querySelectorAll('use')).toHaveLength(8);
    expect(map.querySelectorAll('.scanzone svg.scan use')).toHaveLength(2);
    expect(map.querySelectorAll('[style]')).toHaveLength(0);
    expect(map.querySelectorAll('[tabindex], a, button')).toHaveLength(0);
  });

  it('redraws once, 250ms after the last resize', async () => {
    render(FieldMap, { shown: 'map' });
    await drawn();
    const first = document.querySelector('.ls-fieldmap defs path')!.getAttribute('d');

    vi.useFakeTimers();
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 });
    window.dispatchEvent(new Event('resize'));
    vi.advanceTimersByTime(200);
    window.dispatchEvent(new Event('resize'));
    vi.advanceTimersByTime(200);
    await tick();
    expect(document.querySelector('.ls-fieldmap defs path')!.getAttribute('d')).toBe(first);

    vi.advanceTimersByTime(60);
    await tick();
    expect(document.querySelector('.ls-fieldmap svg')!.getAttribute('viewBox')).toMatch(/^0 0 500 /);
  });
});
