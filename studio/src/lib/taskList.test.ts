// A task list opens with the query saved for its view and keeps each change,
// so the TUI and 'task list' start from the filter set here. The default is
// kept as nothing, and a query the daemon refused is not kept at all.
import { beforeEach, describe, expect, it, vi } from 'vitest';

const asked: string[][] = [];
const saved: [string, string][] = [];

vi.mock('./api', async (original) => {
  const real = await original<typeof import('./api')>();

  return {
    ...real,
    readJson: vi.fn(async (_path: string, words: string[]) => {
      asked.push(words);

      if (words.some((word) => word.includes('colour'))) {
        throw new real.Refused("'colour' is not something tasks can be filtered on.", 400);
      }

      return { tasks: [], recorded: 0, query: { text: '', parts: [], fields: [], defaultQuery: '-state:done,dropped' } };
    }),
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
});
