// Splitter: the divider every split shares. It is a separator that takes
// focus and says where it is; the arrows move it a little and Shift with
// them further, Home and End go to its limits, Enter and a double click put
// it back; a drag moves it by the distance over the length it shares, held
// to its limits; and letting go, or a key, tells the terminals to send
// their size. Where two cross, a drag moves both.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import SplitCross from './SplitCross.svelte';
import Splitter from './Splitter.svelte';
import { SPLIT_SETTLED } from '../splits';

/** Rendered inside a box 1000 pixels each way, the length its fractions share. */
function divider(props: Record<string, unknown> = {}) {
  const host = document.body.appendChild(document.createElement('div'));
  host.getBoundingClientRect = () => ({ width: 1000, height: 1000, top: 0, left: 0, right: 1000, bottom: 1000, x: 0, y: 0, toJSON: () => ({}) });
  const onchange = vi.fn();
  render(Splitter, { target: host, props: { value: 0.5, min: 0.2, max: 0.8, reset: 0.5, label: 'Width of the panes', onchange, ...props } });

  return { onchange, node: screen.getByRole('separator', { name: 'Width of the panes' }) };
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('Splitter', () => {
  it('is a separator that takes focus and says where it is and how far it goes', () => {
    const { node } = divider({ value: 0.42 });

    expect(node.getAttribute('tabindex')).toBe('0');
    expect(node.getAttribute('aria-orientation')).toBe('vertical');
    expect(node.getAttribute('aria-valuenow')).toBe('42');
    expect(node.getAttribute('aria-valuemin')).toBe('20');
    expect(node.getAttribute('aria-valuemax')).toBe('80');
  });

  it('moves a little on the arrows across it, and further with Shift', async () => {
    const { node, onchange } = divider();

    await fireEvent.keyDown(node, { key: 'ArrowRight' });
    expect(onchange).toHaveBeenLastCalledWith(0.52);
    await fireEvent.keyDown(node, { key: 'ArrowLeft' });
    expect(onchange).toHaveBeenLastCalledWith(0.48);
    await fireEvent.keyDown(node, { key: 'ArrowRight', shiftKey: true });
    expect(onchange).toHaveBeenLastCalledWith(0.6);
  });

  it('takes the up and down arrows when it lies between panes one above the other, and only those', async () => {
    const { node, onchange } = divider({ orientation: 'horizontal' });

    await fireEvent.keyDown(node, { key: 'ArrowRight' });
    expect(onchange).not.toHaveBeenCalled();
    await fireEvent.keyDown(node, { key: 'ArrowDown' });
    expect(onchange).toHaveBeenLastCalledWith(0.52);
    await fireEvent.keyDown(node, { key: 'ArrowUp' });
    expect(onchange).toHaveBeenLastCalledWith(0.48);
  });

  it('goes to its limits on Home and End, and back on Enter', async () => {
    const { node, onchange } = divider({ value: 0.3, reset: 0.6 });

    await fireEvent.keyDown(node, { key: 'Home' });
    expect(onchange).toHaveBeenLastCalledWith(0.2);
    await fireEvent.keyDown(node, { key: 'End' });
    expect(onchange).toHaveBeenLastCalledWith(0.8);
    await fireEvent.keyDown(node, { key: 'Enter' });
    expect(onchange).toHaveBeenLastCalledWith(0.6);
  });

  it('is held to its limits', async () => {
    const { node, onchange } = divider({ value: 0.79 });

    await fireEvent.keyDown(node, { key: 'ArrowRight', shiftKey: true });

    expect(onchange).toHaveBeenLastCalledWith(0.8);
  });

  it('leaves Ctrl+Alt with the arrows to the page, which moves between panes on them', async () => {
    const { node, onchange } = divider();
    const page = vi.fn();
    document.body.addEventListener('keydown', page);

    await fireEvent.keyDown(node, { key: 'ArrowRight', ctrlKey: true, altKey: true });

    expect(onchange).not.toHaveBeenCalled();
    expect(page).toHaveBeenCalledOnce();
    document.body.removeEventListener('keydown', page);
  });

  it('keeps the keys it uses from the page around it', async () => {
    const { node } = divider();
    const page = vi.fn();
    document.body.addEventListener('keydown', page);

    await fireEvent.keyDown(node, { key: 'ArrowRight' });
    await fireEvent.keyDown(node, { key: 'Home' });

    expect(page).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', page);
  });

  it('follows a drag by the distance over the length it shares, held to its limits, and settles when let go', async () => {
    const { node, onchange } = divider();
    const settled = vi.fn();
    document.addEventListener(SPLIT_SETTLED, settled);

    await fireEvent.pointerDown(node, { button: 0, pointerId: 1, clientX: 500 });
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 600 });
    expect(onchange).toHaveBeenLastCalledWith(0.6);
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 1000 });
    expect(onchange).toHaveBeenLastCalledWith(0.8);
    expect(node.classList.contains('dragging')).toBe(true);
    expect(settled).not.toHaveBeenCalled();

    await fireEvent.pointerUp(node, { pointerId: 1, clientX: 1000 });
    expect(settled).toHaveBeenCalledOnce();
    expect(node.classList.contains('dragging')).toBe(false);

    // Moving after it was let go does nothing.
    onchange.mockClear();
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 300 });
    expect(onchange).not.toHaveBeenCalled();
    document.removeEventListener(SPLIT_SETTLED, settled);
  });

  it('ignores a button other than the main one', async () => {
    const { node, onchange } = divider();

    await fireEvent.pointerDown(node, { button: 2, pointerId: 1, clientX: 500 });
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 600 });

    expect(onchange).not.toHaveBeenCalled();
  });

  it('settles when a key that moved it is let go, and goes back on a double click', async () => {
    const { node, onchange } = divider({ value: 0.3, reset: 0.5 });
    const settled = vi.fn();
    document.addEventListener(SPLIT_SETTLED, settled);

    await fireEvent.keyUp(node, { key: 'ArrowRight' });
    expect(settled).toHaveBeenCalledOnce();
    await fireEvent.keyUp(node, { key: 'a' });
    expect(settled).toHaveBeenCalledOnce();

    await fireEvent.dblClick(node);
    expect(onchange).toHaveBeenLastCalledWith(0.5);
    expect(settled).toHaveBeenCalledTimes(2);
    document.removeEventListener(SPLIT_SETTLED, settled);
  });
});

describe('SplitCross', () => {
  /** Inside a box 1000 pixels wide and 500 high. */
  function crossing(props: Record<string, unknown> = {}) {
    const host = document.body.appendChild(document.createElement('div'));
    host.getBoundingClientRect = () => ({ width: 1000, height: 500, top: 0, left: 0, right: 1000, bottom: 500, x: 0, y: 0, toJSON: () => ({}) });
    const onchange = vi.fn();
    render(SplitCross, { target: host, props: { column: 0.5, row: 0.5, colMin: 0.2, colMax: 0.8, rowMin: 0.3, rowMax: 0.7, reset: [0.5, 0.5], onchange, ...props } });

    return { onchange, node: host.querySelector<HTMLElement>('.ls-cross')! };
  }

  it('moves both dividers by a drag, each by its own length and held to its own limits', async () => {
    const { node, onchange } = crossing();

    await fireEvent.pointerDown(node, { button: 0, pointerId: 1, clientX: 500, clientY: 250 });
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 600, clientY: 300 });
    expect(onchange).toHaveBeenLastCalledWith(0.6, 0.6);
    await fireEvent.pointerMove(node, { pointerId: 1, clientX: 0, clientY: 500 });
    expect(onchange).toHaveBeenLastCalledWith(0.2, 0.7);
  });

  it('settles when let go, puts both back on a double click, and is not offered to a screen reader twice', async () => {
    const { node, onchange } = crossing({ column: 0.3, row: 0.6 });
    const settled = vi.fn();
    document.addEventListener(SPLIT_SETTLED, settled);

    await fireEvent.pointerDown(node, { button: 0, pointerId: 1, clientX: 300, clientY: 300 });
    await fireEvent.pointerUp(node, { pointerId: 1 });
    expect(settled).toHaveBeenCalledOnce();

    await fireEvent.dblClick(node);
    expect(onchange).toHaveBeenLastCalledWith(0.5, 0.5);
    expect(settled).toHaveBeenCalledTimes(2);
    expect(node.getAttribute('aria-hidden')).toBe('true');
    expect(node.hasAttribute('tabindex')).toBe(false);
    document.removeEventListener(SPLIT_SETTLED, settled);
  });
});
