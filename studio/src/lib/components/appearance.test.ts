// Appearance's Background group: its three options, the command it shows,
// the line that says what overrides it, and a pick going to the setting.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Appearance from '../../pages/Appearance.svelte';
import { studio } from '../state.svelte';
import { paletteGroups } from '../paletteGroups';
import type { Preferences } from '../api';

describe('Appearance', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    studio.prefs = null;
  });

  it('offers the background as Plain, Field map still and Field map animated, with its command', () => {
    studio.prefs = { backdrop: 'map' } as Preferences;

    render(Appearance);

    const group = screen.getByRole('group', { name: 'Background' });
    const options = [...group.querySelectorAll<HTMLInputElement>('input[type="radio"]')];

    expect(options.map((option) => [option.value, option.parentElement!.textContent!.trim(), option.checked])).toEqual([
      ['plain', 'Plain', false],
      ['map', 'Field map, still', true],
      ['map-animated', 'Field map, animated', false],
    ]);
    expect(group.querySelector('code')!.textContent).toBe('$ loadout config set studio-backdrop map');
    expect(group.textContent).toContain('Pure Focus, Ambient and high contrast keep the plain ground.');
  });

  it('reads an unset background as plain', () => {
    studio.prefs = {} as Preferences;

    render(Appearance);

    const group = screen.getByRole('group', { name: 'Background' });
    expect(group.querySelector<HTMLInputElement>('input[value="plain"]')!.checked).toBe(true);
  });

  it('comes after Motion', () => {
    render(Appearance);

    const legends = [...document.querySelectorAll('legend')].map((legend) => legend.textContent);
    expect(legends.indexOf('Background')).toBe(legends.indexOf('Motion') + 1);
  });

  it('keeps a pick in the studio-backdrop setting', async () => {
    const set = vi.spyOn(studio, 'setPreference').mockResolvedValue();
    render(Appearance);

    await fireEvent.click(screen.getByLabelText('Field map, animated'));

    expect(set).toHaveBeenCalledWith('studio-backdrop', 'map-animated');
  });

  it('is named in the palette’s Appearance entry', () => {
    const none = () => {};
    const rows = paletteGroups(studio, none, { launch: none, ambient: none, appearance: none }).flatMap((group) => group.items);

    expect(rows.find((row) => row.key === 'do-appearance')?.sub).toContain('background');
  });
});
