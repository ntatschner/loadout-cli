// A task list opens with the query saved for its view and keeps each change,
// so the TUI and 'task list' start from the filter set here. The default is
// kept as nothing, and a query the daemon refused is not kept at all.
import { beforeEach, describe, expect, it, vi } from 'vitest';

const asked: string[][] = [];
const saved: [string, string][] = [];
const changed: unknown[][] = [];
let stale = false;

vi.mock('./api', async (original) => {
  const real = await original<typeof import('./api')>();

  return {
    ...real,
    readJson: vi.fn(async (_path: string, words: string[]) => {
      asked.push(words);

      if (words.some((word) => word.includes('colour'))) {
        throw new real.Refused("'colour' is not something tasks can be filtered on.", 400);
      }

      return { tasks: [{ id: 'a', title: 'A', state: 'open' }, { id: 'b', title: 'B', state: 'open' }], recorded: 2, revision: 'r1', query: { text: '', parts: [], fields: [], defaultQuery: '-state:done,dropped' } };
    }),
    tasks: {
      ...real.tasks,
      change: vi.fn(async (...given: unknown[]) => {
        changed.push(given);

        if (stale) {
          throw new real.Refused('website has changed since it was read', 409);
        }

        return { project: 'p', revision: 'r2', task: { id: 'a', title: 'A', state: 'done', kind: 'task', by: 'nigel', declared: '', note: '', moved: null } };
      }),
    },
  };
});

vi.mock('./state.svelte', () => ({
  studio: {
    watchOnly: false,
    prefs: { views: { tasks: 'state:open' } },
    setPreference: vi.fn(async (key: string, value: string) => {
      saved.push([key, value]);
    }),
  },
}));

const { TaskListView } = await import('./taskList.svelte');

describe('TaskListView', () => {
  beforeEach(() => {
    asked.length = 0;
    saved.length = 0;
  });

  it('opens with the saved query', async () => {
    const list = new TaskListView();

    await list.load(['--project', 'p']);

    expect(asked).toEqual([['--project', 'p', '--filter=state:open']]);
    expect(list.text).toBe('state:open');
  });

  it('keeps a change, and keeps the default as nothing', async () => {
    const list = new TaskListView();
    await list.load(['--project', 'p']);

    await list.ask('state:doing');
    await list.ask('-state:done,dropped');

    expect(saved).toEqual([['studio-query-tasks', 'state:doing'], ['studio-query-tasks', '']]);
  });

  it('does not keep a query the daemon refused, and says why', async () => {
    const list = new TaskListView();
    await list.load(['--project', 'p']);

    await list.ask('colour:red');

    expect(saved).toEqual([]);
    expect(list.problem).toContain("'colour' is not something");
  });

  it('keeps nothing for a list with no view', async () => {
    const list = new TaskListView(null);
    await list.load(['--global']);

    await list.ask('state:doing');

    expect(asked[0]).toEqual(['--global']);
    expect(saved).toEqual([]);
  });

  it('changes a task through the route, from the revision it read, and reads the list again', async () => {
    changed.length = 0;
    const list = new TaskListView();
    await list.load(['--project', 'p']);
    asked.length = 0;

    expect(await list.change({ id: 'a', title: 'A', state: 'open' }, 'done', ['--project', 'p'])).toBe('changed');

    expect(changed).toEqual([['p', 'a', { state: 'done' }, 'r1']]);
    expect(asked.length).toBe(1);
  });

  it('says a change from a stale read was refused, and reads the list again', async () => {
    stale = true;
    const list = new TaskListView();
    await list.load(['--global']);
    asked.length = 0;

    expect(await list.change({ id: 'a', title: 'A', state: 'open' }, 'done', ['--global'])).toBe('stale');

    expect(asked.length).toBe(1);
    expect(changed.at(-1)?.[0]).toBe('-');
    stale = false;
  });

  it('keeps the revision it read, and shows a change as it was answered with the revision it left', async () => {
    const list = new TaskListView();
    await list.load(['--project', 'p']);

    expect(list.revision).toBe('r1');

    list.applied({ id: 'A', title: 'A', state: 'done' }, 'r2');

    expect(list.tasks?.map((one) => `${one.id} ${one.state}`)).toEqual(['A done', 'b open']);
    expect(list.revision).toBe('r2');
  });
});
