// Releasing from the Teams page: the plan read first, the version and notes
// taken from it and edited, and nothing run until a second press that says
// what will be pushed.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Release, { cutWords, type ReleasePlanRead } from './Release.svelte';

const plan = (over: Partial<ReleasePlanRead> = {}): ReleasePlanRead => ({
  project: 'widget',
  recipe: 'tag-push',
  branch: 'main',
  lastTag: 'v1.2.0',
  versionFile: 'Directory.Build.props',
  fileVersion: '1.2.0',
  proposed: '1.3.0',
  bump: 'minor: there is new work that is not a repair',
  since: ['Widgets: add the blue one (#5)'],
  notes: 'Say what changed.\n\nPull requests: #5',
  problems: [],
  ...over,
});

async function readFor(over: Partial<ReleasePlanRead> = {}) {
  const acted: [string, string[]][] = [];
  render(Release, {
    projects: ['widget'],
    read: async () => plan(over),
    act: (path: string, words: string[]) => { acted.push([path, words]); },
  });

  await fireEvent.input(screen.getByLabelText('Project'), { target: { value: 'widget' } });
  await fireEvent.click(screen.getByRole('button', { name: 'Read the plan' }));
  await screen.findByText(/releases with/);

  return acted;
}

describe('Release', () => {
  it('fills in the proposal, and releases only on the second press with what was edited', async () => {
    const acted = await readFor();

    expect((screen.getByLabelText('Version') as HTMLInputElement).value).toBe('1.3.0');
    await fireEvent.input(screen.getByLabelText('Notes'), { target: { value: 'Blue widgets.' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Release…' }));
    expect(acted).toEqual([]);
    expect(screen.getByText(/Bumps Directory.Build.props to 1.3.0/)).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: 'Release v1.3.0' }));
    expect(acted).toEqual([['release cut', ['widget', '--version=1.3.0', '--notes=Blue widgets.', '--yes']]]);
  });

  it('offers no release while the plan says something stops it, and says what', async () => {
    await readFor({ problems: ['The checkout has changes not committed.'] });

    expect(screen.getAllByText('The checkout has changes not committed.').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Release…' })).toBeNull();
  });

  it('keeps a value starting with a dash a value', () => {
    expect(cutWords('widget', ' 1.3.0 ', '- a list')).toEqual(['widget', '--version=1.3.0', '--notes=- a list', '--yes']);
  });
});
