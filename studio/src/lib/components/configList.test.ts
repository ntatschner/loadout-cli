// ConfigList: a search finds a setting by name or by what it does and opens
// the groups it matches in; Set it appears only once a value changes, and
// Clear it sets the empty value that puts a setting back to its default.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import ConfigList from './ConfigList.svelte';
import type { ConfigGroup } from '../api';

const GROUPS: ConfigGroup[] = [
  {
    group: 'Launching',
    settings: [
      { name: 'default-agent', value: '', description: 'The agent a launch starts', machineLocal: false, flag: false, whenUnset: 'claude' },
      { name: 'offline', value: '', description: 'Never contact the network.', machineLocal: true, flag: true, whenUnset: null },
    ],
  },
  {
    group: 'Studio',
    settings: [
      { name: 'studio-accent', value: 'cyan', description: 'The colour Studio lights with.', machineLocal: false, flag: false, whenUnset: 'cyan' },
      { name: 'studio-idle-after', value: '', description: 'Minutes before a quiet session reads idle.', machineLocal: false, flag: false, whenUnset: '10' },
    ],
  },
];

const said = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent ?? '';

describe('ConfigList', () => {
  it('finds a setting by name or by what it does, and opens its group', async () => {
    const shown = render(ConfigList, { groups: GROUPS });
    const groups = () => [...shown.container.querySelectorAll('details.grp')] as HTMLDetailsElement[];

    expect(screen.getByText('4 settings')).toBeTruthy();
    expect(groups().map((one) => one.open)).toEqual([true, false]);

    await fireEvent.input(screen.getByLabelText('Find a setting'), { target: { value: 'quiet session' } });
    expect(screen.getByText('1 of 4 settings')).toBeTruthy();
    expect(groups()).toHaveLength(1);
    expect(groups()[0].open).toBe(true);
    expect(within(groups()[0]).getByText('studio-idle-after')).toBeTruthy();

    await fireEvent.input(screen.getByLabelText('Find a setting'), { target: { value: 'DEFAULT-AG' } });
    expect(within(groups()[0]).getByText('default-agent')).toBeTruthy();
  });

  it('reads the description and what an unset setting uses as two sentences', () => {
    render(ConfigList, { groups: GROUPS });

    const row = screen.getByRole('group', { name: 'default-agent' });
    expect(row.querySelector('.d')?.textContent).toBe('The agent a launch starts. Unset, it uses claude.');
  });

  it('offers Set it only once a value changes, and Put it back undoes it', async () => {
    const set: [string, string][] = [];
    render(ConfigList, { groups: GROUPS, onset: (name: string, value: string) => set.push([name, value]) });

    const row = screen.getByRole('group', { name: 'default-agent' });
    expect(within(row).queryByRole('button', { name: 'Set it' })).toBeNull();

    await fireEvent.input(within(row).getByLabelText('default-agent'), { target: { value: 'codex' } });
    const button = within(row).getByRole('button', { name: 'Set it' });
    expect(said(button)).toBe('$ loadout config set default-agent codex');

    await fireEvent.click(within(row).getByRole('button', { name: 'Put it back' }));
    expect(within(row).queryByRole('button', { name: 'Set it' })).toBeNull();

    // Typed back to what is stored, it is no change.
    await fireEvent.input(within(row).getByLabelText('default-agent'), { target: { value: 'x' } });
    await fireEvent.input(within(row).getByLabelText('default-agent'), { target: { value: '' } });
    expect(within(row).queryByRole('button', { name: 'Set it' })).toBeNull();

    await fireEvent.input(within(row).getByLabelText('default-agent'), { target: { value: 'codex' } });
    await fireEvent.click(within(row).getByRole('button', { name: 'Set it' }));
    expect(set).toEqual([['default-agent', 'codex']]);
  });

  it('clears a set value with the empty value', async () => {
    const set: [string, string][] = [];
    render(ConfigList, { groups: GROUPS, onset: (name: string, value: string) => set.push([name, value]) });

    await fireEvent.click(screen.getByText('Studio'));
    const row = screen.getByRole('group', { name: 'studio-accent' });
    const clear = within(row).getByRole('button', { name: 'Clear it' });

    expect(said(clear)).toBe("$ loadout config set studio-accent ''");
    await fireEvent.click(clear);
    expect(set).toEqual([['studio-accent', '']]);
  });
});
