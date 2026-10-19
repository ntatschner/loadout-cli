// The palette's global search: asked only once two characters are typed and
// typing has paused, its groups after the palette's own, and each hit going
// to where Studio shows it.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import CommandPalette from './CommandPalette.svelte';
import { foundGroups, type Found } from '../search';

const FOUND: Found = {
  groups: [
    { kind: 'task', hits: [{ id: 'fix-login', title: 'Fix the login page', project: 'web', state: 'open', updated: null, open: '' }], more: 0 },
    { kind: 'idea', hits: [{ id: 'sso', title: 'Single sign-on', project: null, state: 'captured', updated: null, open: '' }], more: 0 },
    { kind: 'run', hits: [{ id: 'r1', title: 'Ship it', project: 'web', state: 'running', updated: null, open: '' }], more: 0 },
  ],
};

describe('palette search', () => {
  it('sends each kind of hit to where it is shown', () => {
    const went: string[][] = [];
    const groups = foundGroups(FOUND, (...parts) => went.push(parts));

    groups.flatMap((group) => group.items).forEach((row) => row.run?.(row));

    expect(groups.map((group) => group.name)).toEqual(['Tasks found', 'Ideas found', 'Team runs found']);
    expect(went).toEqual([['projects', 'web'], ['ideas', '~', 'sso'], ['teams', 'r1']]);
  });

  it('asks after a pause, from two characters, and lists what it found after its own rows', async () => {
    vi.useFakeTimers();

    try {
      const asked: string[] = [];
      const search = vi.fn(async (query: string) => {
        asked.push(query);

        return foundGroups(FOUND, () => undefined);
      });
      render(CommandPalette, { groups: [{ name: 'Go to', items: [{ key: 'home', label: 'Home login', sub: 'page' }] }], search });
      const box = screen.getByRole('combobox');

      await fireEvent.input(box, { target: { value: 'l' } });
      await vi.advanceTimersByTimeAsync(300);
      expect(asked).toEqual([]);

      await fireEvent.input(box, { target: { value: 'lo' } });
      await fireEvent.input(box, { target: { value: 'login' } });
      await vi.advanceTimersByTimeAsync(300);

      expect(asked).toEqual(['login']);
      expect(screen.getAllByRole('group').map((group) => group.querySelector('h3')?.textContent)).toEqual(['Go to', 'Tasks found', 'Ideas found', 'Team runs found']);
      expect(screen.getByRole('option', { name: /Fix the login page/ })).toBeTruthy();
    } finally {
      vi.useRealTimers();
    }
  });
});
