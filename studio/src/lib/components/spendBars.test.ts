// SpendBars: never more than twenty bars, saying how many were left out;
// by day, the arrow keys, Home and End move the readout along the columns.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import SpendBars from './SpendBars.svelte';

describe('SpendBars', () => {
  it('shows the twenty largest and says how many smaller ones are left out', () => {
    const groups = Array.from({ length: 27 }, (_, index) => ({ name: `p${index}`, value: (index + 1) * 1000 }));
    const shown = render(SpendBars, { groups, by: 'project' });

    const rows = [...shown.container.querySelectorAll('ol.rows li')];
    expect(rows).toHaveLength(20);
    expect(rows[0].querySelector('.nm')?.textContent).toBe('p26');
    expect(screen.getByText('The 7 smallest are left out here.')).toBeTruthy();
  });

  it('marks a folder that is not a project', () => {
    render(SpendBars, { groups: [{ name: 'D:\\scratch', value: 10, registered: false }], by: 'project' });

    expect(screen.getByText('not a project')).toBeTruthy();
  });

  it('by day, reads each day with the arrow keys, Home and End', async () => {
    const groups = [
      { name: '2026-10-15', value: 1000 },
      { name: '2026-10-16', value: 2500 },
      { name: '2026-10-17', value: 4000 },
    ];
    render(SpendBars, { groups, by: 'day' });

    const chart = screen.getByRole('group');
    const readout = () => document.querySelector('.readout')?.textContent;

    expect(readout()).toBe('2026-10-17 · 4k tokens');
    await fireEvent.keyDown(chart, { key: 'ArrowLeft' });
    expect(readout()).toBe('2026-10-16 · 2.5k tokens');
    await fireEvent.keyDown(chart, { key: 'Home' });
    expect(readout()).toBe('2026-10-15 · 1k tokens');
    await fireEvent.keyDown(chart, { key: 'ArrowLeft' });
    expect(readout()).toBe('2026-10-15 · 1k tokens');
    await fireEvent.keyDown(chart, { key: 'End' });
    expect(readout()).toBe('2026-10-17 · 4k tokens');
  });
});
