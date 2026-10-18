// A page that picks the first project for you must not do it after it has
// gone: a read that ends late once sent the browser from Projects to
// Context, because a Context made earlier had asked for the project list.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/svelte';
import { tick } from 'svelte';

let release: (() => void) | null = null;

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();
  const later = <T>(value: T) => new Promise<T>((resolve) => (release = () => resolve(value)));

  return {
    ...real,
    readJson: vi.fn(async (path: string) => (path === 'project list' ? later({ projects: [{ id: 'starstats', name: 'starstats' }] }) : new Promise(() => undefined))),
    memory: { ...real.memory, projects: vi.fn(() => later([{ slug: 'starstats', name: 'starstats' }])) },
  };
});

const { router } = await import('../lib/router.svelte');
const { default: Context } = await import('./Context.svelte');
const { default: Memory } = await import('./Memory.svelte');

describe('choosing the first project', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    release = null;
  });

  it('does not send the browser to Context once that page has gone', async () => {
    const go = vi.spyOn(router, 'go').mockImplementation(() => undefined);
    router.route = { page: 'context', rest: [] };

    const shown = render(Context, { rest: [] });
    shown.unmount();
    router.route = { page: 'projects', rest: [] };
    release?.();
    await tick();
    await Promise.resolve();

    expect(go).not.toHaveBeenCalled();
  });

  it('does not send the browser to Memory once that page has gone', async () => {
    const go = vi.spyOn(router, 'go').mockImplementation(() => undefined);
    router.route = { page: 'memory', rest: [] };

    const shown = render(Memory, { slug: null, scope: null, name: null });
    shown.unmount();
    release?.();
    await tick();
    await Promise.resolve();

    expect(go).not.toHaveBeenCalled();
  });

  it('still chooses the first project while it is on the page', async () => {
    const go = vi.spyOn(router, 'go').mockImplementation(() => undefined);
    router.route = { page: 'context', rest: [] };

    render(Context, { rest: [] });
    release?.();
    await tick();
    await Promise.resolve();

    expect(go).toHaveBeenCalledWith('context', 'starstats');
  });
});
