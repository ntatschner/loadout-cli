// Tabs, ModePicker and Picker: choosing one of several, by pointer and by
// the keys their READMEs promise.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Tabs from './Tabs.svelte';
import ModePicker from './ModePicker.svelte';
import Picker from './Picker.svelte';

describe('Tabs', () => {
  const tabs = [{ label: 'Lens' }, { label: 'Map' }, { label: 'Ambient' }];

  it('keeps only the selected tab in the tab order', () => {
    render(Tabs, { tabs, selected: 1, label: 'Home view' });

    const [lens, map, ambient] = screen.getAllByRole('tab');
    expect(map.getAttribute('aria-selected')).toBe('true');
    expect(map.tabIndex).toBe(0);
    expect(lens.tabIndex).toBe(-1);
    expect(ambient.tabIndex).toBe(-1);
  });

  it('moves and selects with the arrow keys, Home and End, wrapping at the ends', async () => {
    const onselect = vi.fn();
    render(Tabs, { tabs, selected: 0, onselect });

    const all = screen.getAllByRole('tab');
    await fireEvent.keyDown(all[0], { key: 'ArrowLeft' });
    expect(onselect).toHaveBeenLastCalledWith(2);
    expect(document.activeElement).toBe(all[2]);

    await fireEvent.keyDown(all[2], { key: 'ArrowRight' });
    expect(onselect).toHaveBeenLastCalledWith(0);

    await fireEvent.keyDown(all[0], { key: 'End' });
    expect(onselect).toHaveBeenLastCalledWith(2);

    await fireEvent.keyDown(all[2], { key: 'Home' });
    expect(onselect).toHaveBeenLastCalledWith(0);
    expect(all[0].getAttribute('aria-selected')).toBe('true');
  });

  it('ties the panel to the selected tab', async () => {
    render(Tabs, { tabs, selected: 0 });

    await fireEvent.click(screen.getByRole('tab', { name: 'Map' }));

    expect(screen.getByRole('tabpanel').getAttribute('aria-labelledby')).toBe(screen.getByRole('tab', { name: 'Map' }).id);
  });
});

describe('ModePicker', () => {
  const options = [
    { name: 'advise', desc: 'Answer the question.' },
    { name: 'implement', desc: 'Make the change.' },
  ];

  it('is a radio group whose command follows the pick', async () => {
    const onchange = vi.fn();
    render(ModePicker, { options, label: 'Mode', command: (name: string) => `$ loadout --mode ${name}`, onchange });

    expect(screen.getByRole('radiogroup', { name: 'Mode' })).toBeTruthy();
    expect(screen.getByText('$ loadout --mode advise')).toBeTruthy();

    await fireEvent.click(screen.getByRole('radio', { name: /implement/ }));

    expect(onchange).toHaveBeenCalledWith('implement', 1);
    expect(screen.getByText('$ loadout --mode implement')).toBeTruthy();
    expect(screen.getByRole('radio', { name: /implement/ }).getAttribute('aria-checked')).toBe('true');
  });

  it('picks as the arrow keys move', async () => {
    render(ModePicker, { options, command: (name: string) => `mode ${name}` });

    await fireEvent.keyDown(screen.getByRole('radio', { name: /advise/ }), { key: 'ArrowDown' });

    expect(screen.getByRole('radio', { name: /implement/ }).getAttribute('aria-checked')).toBe('true');
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: /implement/ }));
  });
});

describe('Picker', () => {
  const options = [{ name: 'starstats' }, { name: 'loadout-cli', sub: '~/code/loadout-cli' }, { name: 'workspace' }];

  it('opens a listbox from its control and chooses with Enter', async () => {
    const onpick = vi.fn();
    render(Picker, { label: 'Project', options, onpick });

    const trigger = screen.getByRole('button', { name: /Project/ });
    expect(trigger.getAttribute('aria-expanded')).toBe('false');

    await fireEvent.click(trigger);
    const list = screen.getByRole('listbox');
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(list);

    await fireEvent.keyDown(list, { key: 'ArrowDown' });
    expect(list.getAttribute('aria-activedescendant')).toBe(screen.getAllByRole('option')[1].id);

    await fireEvent.keyDown(list, { key: 'Enter' });

    expect(onpick).toHaveBeenCalledWith(options[1], 1);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    expect(trigger.textContent).toContain('loadout-cli');
  });

  it('closes on Escape without choosing, and returns focus', async () => {
    const onpick = vi.fn();
    render(Picker, { label: 'Project', options, onpick });

    const trigger = screen.getByRole('button', { name: /Project/ });
    await fireEvent.click(trigger);
    await fireEvent.keyDown(screen.getByRole('listbox'), { key: 'End' });
    await fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' });

    expect(onpick).not.toHaveBeenCalled();
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('closes on a click outside', async () => {
    render(Picker, { label: 'Project', options });

    await fireEvent.click(screen.getByRole('button', { name: /Project/ }));
    await fireEvent.mouseDown(document.body);

    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('opens from the keyboard with an arrow', async () => {
    render(Picker, { label: 'Project', options });

    await fireEvent.keyDown(screen.getByRole('button', { name: /Project/ }), { key: 'ArrowDown' });

    expect(screen.getByRole('listbox')).toBeTruthy();
  });
});
