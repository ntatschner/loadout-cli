// The filter bar over Home's Map and Lens: which preset it names, what the
// project summary says is hidden, and that the last kind or state stays on.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import MapFilterBar from './MapFilterBar.svelte';
import { NO_PROJECT, PRESETS, type MapFilter } from '../mapFilter';

const bar = (filter: MapFilter, projects = ['starstats', NO_PROJECT]) => {
  const onchange = vi.fn();

  render(MapFilterBar, { filter, projects, onchange });

  return onchange;
};

describe('the map filter bar', () => {
  it('names the preset in force, and says Custom when the chips match none', () => {
    bar(PRESETS.inflight);

    expect(screen.getByRole('button', { name: 'In flight' }).getAttribute('aria-pressed')).toBe('true');
    expect(screen.queryByText('Custom')).toBeNull();
  });

  it('says Custom once a chip moves away from every preset', () => {
    bar({ ...PRESETS.all, kinds: ['ideas'] });

    expect(screen.getByText('Custom')).toBeTruthy();
    expect(screen.getAllByRole('button', { pressed: true }).map((button) => button.textContent)).not.toContain('Everything');
  });

  it('shows (no project) as hidden in the project summary', () => {
    bar({ ...PRESETS.inflight, projects: [NO_PROJECT] });

    expect(screen.getByText(`Projects · ${NO_PROJECT} hidden`)).toBeTruthy();
  });

  it('writes Everything from its preset', async () => {
    const onchange = bar(PRESETS.inflight);

    await fireEvent.click(screen.getByRole('button', { name: 'Everything' }));

    expect(onchange).toHaveBeenCalledWith(PRESETS.all);
  });

  it('keeps the last state on', async () => {
    const onchange = bar({ ...PRESETS.all, states: ['ended'] });

    await fireEvent.click(within(screen.getByRole('group', { name: 'State' })).getByRole('button', { name: 'Ended' }));

    expect(onchange).toHaveBeenCalledWith({ ...PRESETS.all, states: ['ended'] });
  });

  it('hides a project by its chip', async () => {
    const onchange = bar(PRESETS.inflight);

    await fireEvent.click(within(screen.getByRole('group', { name: 'Projects shown' })).getByRole('button', { name: 'starstats' }));

    expect(onchange).toHaveBeenCalledWith({ ...PRESETS.inflight, projects: ['starstats'] });
  });
});
