// After a restart the event stream reconnects by itself. Studio then asks
// the daemon what it came back as, and when that is a newer version than
// the one this page was loaded from, it offers a reload, so old code does
// not go on calling a changed daemon.
import { describe, expect, it, vi } from 'vitest';

let back: (() => void) | undefined;
const versions = ['0.69.0', '0.70.0'];

vi.mock('./api', async (original) => {
  const real = await original<typeof import('./api')>();

  return {
    ...real,
    signedIn: vi.fn(async () => true),
    about: vi.fn(async () => ({ version: versions.shift() ?? '0.70.0', hostsSessions: false })),
    update: vi.fn(async () => null),
    listen: vi.fn((_changed: (what: string[]) => void, reconnected?: () => void) => {
      back = reconnected;

      return () => undefined;
    }),
  };
});

const { studio } = await import('./state.svelte');

describe('coming back after the daemon restarts', () => {
  it('offers a reload when the daemon came back as a newer version, and not before', async () => {
    await studio.start();

    expect(studio.newVersion).toBeNull();
    expect(back).toBeTypeOf('function');

    back!();
    await vi.waitFor(() => expect(studio.newVersion).toBe('0.70.0'));

    studio.stop();
  });
});
