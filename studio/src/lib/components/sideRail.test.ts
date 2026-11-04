// A page with no icon of its own falls back to Home's, which reads as two Homes
// in the rail, so every page Studio lists has to bring one.
import app from '../../App.svelte?raw';
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render } from '@testing-library/svelte';
import SideRail from './SideRail.svelte';

const listed = [...app.matchAll(/\{ key: '([a-z]+)', name: '([^']+)'/g)].map(([, key, name]) => ({ key, name, fact: '' }));

describe('the rail', () => {
  afterEach(cleanup);

  it('reads every page from the list Studio really uses', () => {
    expect(listed.map((page) => page.key)).toEqual(expect.arrayContaining(['home', 'sessions', 'settings', 'about']));
  });

  it('gives every page an icon of its own', () => {
    const { container } = render(SideRail, { pages: listed, current: 'home' });
    const drawn = listed.map(({ name }) => {
      const link = [...container.querySelectorAll('a')].find((one) => one.textContent?.includes(name));

      return link?.querySelector('path')?.getAttribute('d') ?? null;
    });

    expect(drawn).not.toContain(null);
    expect(new Set(drawn).size).toBe(listed.length);
  });
});
