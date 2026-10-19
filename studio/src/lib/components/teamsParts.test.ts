// The Teams components driven as a person would: answering a Gate, the five
// depths on keys 1 to 5, the Outbox's tree by keyboard with its filter and
// its viewer per kind of file, and Clear out runs said back first.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet, tick } from 'svelte';
import Bin from './Bin.svelte';
import Gate from './Gate.svelte';
import Outbox from './Outbox.svelte';
import RunDepths from './RunDepths.svelte';

describe('Gate', () => {
  it('sends the answer pressed, and says so once it is taken', async () => {
    const onanswer = vi.fn(() => true);
    render(Gate, { kind: 'question', run: 'r1', gate: 'g1', question: 'Ship it?', options: ['Ship it', 'Hold'], recommendation: 'Ship it', onanswer });

    await fireEvent.click(screen.getByRole('button', { name: /Hold/ }));
    await tick();

    expect(onanswer).toHaveBeenCalledWith({ answer: 'Hold', reason: null });
    expect(screen.getByRole('status').textContent).toBe('✓ You answered: Hold.');
    expect(screen.queryByRole('button', { name: /Ship it/ })).toBeNull();
  });

  it('shows each answer’s command, and offers no Think again on a confirmation', () => {
    render(Gate, { kind: 'confirm', run: 'r1', gate: 'g2', question: 'Merge it?' });

    expect(screen.getByText('$ loadout team gate r1 --gate g2 --answer yes --by dashboard')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Think again/ })).toBeNull();
  });

  it('sends a changed brief instead, and keeps the box as sent', async () => {
    const onanswer = vi.fn(() => true);
    render(Gate, { kind: 'brief', run: 'r1', gate: 'g3', node: 'writer', question: 'Brief writer', brief: 'Fix the README.', onanswer });

    const box = screen.getByRole('textbox', { name: /The brief writer will be given/ }) as HTMLTextAreaElement;
    await fireEvent.input(box, { target: { value: 'Fix the README and the CHANGELOG.' } });
    expect(screen.getByText(/--instead "Fix the README and the CHANGELOG."/)).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: /Send your brief/ }));

    expect(onanswer).toHaveBeenCalledWith({ answer: 'yes', instead: 'Fix the README and the CHANGELOG.' });
    expect(box.readOnly).toBe(true);
  });

  it('stays open when the answer was not taken', async () => {
    render(Gate, { kind: 'confirm', run: 'r1', question: 'Go?', onanswer: () => false });

    await fireEvent.click(screen.getByRole('button', { name: /Yes/ }));

    expect(screen.getByRole('button', { name: /No/ })).toBeTruthy();
  });
});

describe('RunDepths', () => {
  const panel = createRawSnippet((index: () => number) => ({ render: () => `<p>depth ${index()}</p>` }));

  it('picks a depth with keys 1 to 5, and leaves them alone in a field or a dialog', async () => {
    render(RunDepths, { children: panel });

    // Read off the tabs: a raw snippet draws once and does not follow its argument.
    const picked = () => screen.getAllByRole('tab').findIndex((tab) => tab.getAttribute('aria-selected') === 'true');

    await fireEvent.keyDown(document.body, { key: '3' });
    expect(picked()).toBe(2);
    expect(document.activeElement?.textContent).toContain('The papers');

    const input = document.body.appendChild(document.createElement('input'));
    await fireEvent.keyDown(input, { key: '5' });
    expect(picked()).toBe(2);

    const dialog = document.body.appendChild(document.createElement('div'));
    dialog.setAttribute('role', 'dialog');
    const inside = dialog.appendChild(document.createElement('button'));
    await fireEvent.keyDown(inside, { key: '1' });
    expect(picked()).toBe(2);

    input.remove();
    dialog.remove();
  });

  it('shows each depth’s key', () => {
    render(RunDepths, { children: panel });

    expect(screen.getAllByRole('tab').map((tab) => tab.getAttribute('aria-keyshortcuts'))).toEqual(['1', '2', '3', '4', '5']);
  });

  it('does nothing with the keys when single-letter shortcuts are off', async () => {
    render(RunDepths, { children: panel, keys: false });

    await fireEvent.keyDown(document.body, { key: '4' });

    expect(screen.getAllByRole('tab')[0].getAttribute('aria-selected')).toBe('true');
  });
});

describe('Outbox', () => {
  const files = [
    { path: 'docs/README.md', change: 'changed' as const, commit: 'a1b2c3d', node: 'writer' },
    { path: 'data/out.csv', change: 'added' as const, commit: 'a1b2c3d', node: 'writer' },
    { path: 'src/App.cs', change: 'removed' as const, commit: 'e4f5a6b', node: 'coder' },
  ];
  const loose = [{ path: 'PLAN.md', kind: 'plan', bytes: 120, node: 'lead' }];

  const read = vi.fn(async (entry: { path: string }) => {
    if (entry.path.endsWith('.md')) {
      return { text: '# Plan\n\n<script>alert(1)</script>\n\n[ok](https://example.com) [no](file:///etc/passwd)', redacted: 2 };
    }

    if (entry.path.endsWith('.csv')) {
      return { text: 'name,count\nalpha,3\n' };
    }

    return { text: 'class App {}' };
  });

  const items = () => screen.getAllByRole('treeitem').filter((item) => !item.closest('[hidden]'));
  // By its key: a group's text holds its children's, so finding by text picks the group.
  const item = (key: string) => screen.getAllByRole('treeitem').find((one) => one.getAttribute('data-item') === key)!;

  it('walks the tree by keyboard: Down, Up, Right, Left, Home and End', async () => {
    render(Outbox, { run: 'r1', repository: '/repo', files, loose, read });
    await tick();

    const first = items()[0];
    first.focus();
    await fireEvent.keyDown(first, { key: 'ArrowDown' });
    await tick();
    expect(document.activeElement).toBe(items()[1]);

    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowUp' });
    await tick();
    expect(document.activeElement).toBe(items()[0]);

    // Left folds a group, Right opens it again.
    const count = items().length;
    await fireEvent.keyDown(items()[0], { key: 'ArrowLeft' });
    await tick();
    expect(items().length).toBeLessThan(count);
    await fireEvent.keyDown(items()[0], { key: 'ArrowRight' });
    await tick();
    expect(items().length).toBe(count);

    await fireEvent.keyDown(items()[0], { key: 'End' });
    await tick();
    expect(document.activeElement).toBe(items()[items().length - 1]);

    await fireEvent.keyDown(document.activeElement!, { key: 'Home' });
    await tick();
    expect(document.activeElement).toBe(items()[0]);
  });

  it('opens a file with Enter and shows Markdown as elements, never as HTML from the file', async () => {
    const { container } = render(Outbox, { run: 'r1', repository: '/repo', files, loose, read });
    await tick();

    const plan = item('loose:PLAN.md');
    plan.focus();
    await fireEvent.keyDown(plan, { key: 'Enter' });
    await vi.waitFor(() => expect(container.querySelector('.ls-md h1')?.textContent).toBe('Plan'));

    expect(container.querySelector('.vbody script')).toBeNull();
    expect(container.querySelector('.vbody')?.textContent).toContain('<script>alert(1)</script>');
    expect([...container.querySelectorAll('.vbody a')].map((link) => link.getAttribute('href'))).toEqual(['https://example.com']);
    expect(screen.getByText(/2 lines that looked like credentials were taken out/)).toBeTruthy();
    expect(screen.getByText('In a terminal: $ cat PLAN.md')).toBeTruthy();
  });

  it('shows a CSV as a table and a removed file as it was before', async () => {
    const { container } = render(Outbox, { run: 'r1', repository: '/repo', files, loose, read, picked: 'a1b2c3d:data/out.csv' });
    await vi.waitFor(() => expect(container.querySelector('.ls-csv td')?.textContent).toBe('alpha'));

    await fireEvent.click(item('e4f5a6b:src/App.cs').querySelector('.ti')!);
    await vi.waitFor(() => expect(screen.getByText(/e4f5a6b removed it/)).toBeTruthy());
    expect(screen.getByText('In a terminal: $ git -C /repo show e4f5a6b~1:src/App.cs')).toBeTruthy();
  });

  it('names a reported file from the repository, though the run reported its whole path', () => {
    const { container } = render(Outbox, { run: 'r1', repository: 'C:/repo', files: [], loose: [{ path: 'C:/repo/docs/PLAN.md', kind: 'plan', bytes: 12, node: 'planner' }], read });

    expect(container.querySelector('[data-item="loose:C:/repo/docs/PLAN.md"] .nm')?.textContent).toBe('docs/PLAN.md');
  });

  it('filters the tree as you type', async () => {
    render(Outbox, { run: 'r1', repository: '/repo', files, loose, read });

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a file' }), { target: { value: 'csv' } });
    await tick();

    const shown = items().filter((one) => one.getAttribute('aria-expanded') === null && one.hasAttribute('data-item'));
    expect(shown.map((one) => one.getAttribute('data-item'))).toEqual(['a1b2c3d:data/out.csv']);

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a file' }), { target: { value: 'nothing-like-this' } });
    expect(screen.getByText(/No delivered file matches/)).toBeTruthy();
  });

  it('takes the words the list query takes: each one, its node, and a minus to leave out', async () => {
    render(Outbox, { run: 'r1', repository: '/repo', files, loose, read });

    const leaves = () =>
      items().filter((one) => one.getAttribute('aria-expanded') === null && one.hasAttribute('data-item')).map((one) => one.getAttribute('data-item'));

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a file' }), { target: { value: 'writer -csv' } });
    await tick();

    expect(leaves()).toEqual(['a1b2c3d:docs/README.md']);
  });
});

describe('Bin', () => {
  it('says Clear out runs back as a sentence before it goes', async () => {
    const { container } = render(Bin, { entries: [] });

    expect(container.querySelector('.sentence')?.textContent).toBe(
      'Moves the runs older than 30d to the bin, never the newest 20, and never one still going or one that left a branch nothing merged.',
    );
    expect(screen.getByText('$ loadout team runs prune --older-than 30d --keep 20 --dry-run')).toBeTruthy();
  });
});
