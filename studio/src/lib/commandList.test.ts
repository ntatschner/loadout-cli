// A list read through its command with the shared query: its items from the
// key the command keeps them under, the saved query for its view, and a list
// that cannot be read said so rather than left reading forever.
import { beforeEach, describe, expect, it, vi } from 'vitest';

const asked: [string, string[]][] = [];
let failWith: Error | null = null;

vi.mock('./api', async (original) => {
  const real = await original<typeof import('./api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[]) => {
      asked.push([path, words]);

      if (failWith) {
        throw failWith;
      }

      return { projects: [{ id: 'web' }], recorded: 3, movedOn: ['x'], query: { text: 'where:here', parts: ['where:here'], fields: [], defaultQuery: '' } };
    }),
  };
});

const studio = { watchOnly: false, signedOut: false, prefs: { views: { projects: 'where:here' } }, setPreference: vi.fn(async () => undefined) };

vi.mock('./state.svelte', () => ({ studio }));

const { CommandList } = await import('./commandList.svelte');
const { SignedOut } = await import('./api');

describe('CommandList', () => {
  beforeEach(() => {
    asked.length = 0;
    failWith = null;
    studio.signedOut = false;
  });

  it('reads its items from where the command keeps them, with the saved query for its view', async () => {
    const wall = new CommandList<{ id: string }>('project list', 'projects', 'projects');

    await wall.load([]);

    expect(asked).toEqual([['project list', ['--filter=where:here']]]);
    expect(wall.items).toEqual([{ id: 'web' }]);
    expect(wall.recorded).toBe(3);
    expect(wall.read?.movedOn).toEqual(['x']);
    expect(wall.from).toBe('$ loadout project list --filter=where:here');
  });

  it('asks for everything the way the command spells it', async () => {
    const wall = new CommandList<{ id: string }>('task list', 'projects', null, ['--all']);

    await wall.ask('');

    expect(asked).toEqual([['task list', ['--all']]]);
  });

  it('says why a list could not be read rather than reading forever', async () => {
    const wall = new CommandList<{ id: string }>('project list', 'projects', null);
    failWith = new Error('The workspace could not be read.');

    await wall.load([]);

    expect(wall.items).toBeNull();
    expect(wall.error).toBe('The workspace could not be read.');
  });

  it('signs the page out when the daemon has', async () => {
    const wall = new CommandList<{ id: string }>('project list', 'projects', null);
    failWith = new SignedOut();

    await wall.load([]);

    expect(studio.signedOut).toBe(true);
  });
});
