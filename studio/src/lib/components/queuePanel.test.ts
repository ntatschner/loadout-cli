// QueuePanel: a team's queue shows what waits and what would be refused, the
// draft pull request grant first and with a way to turn it off, and every
// action runs the queue command written under its button.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import QueuePanel, { type QueuePlan, type QueueRead } from './QueuePanel.svelte';

const NOW = Date.parse('2026-10-22T08:00:00Z');

function queue(over: Partial<QueueRead> = {}): QueueRead {
  return {
    team: 'builders',
    items: [
      { position: 1, task: 'fix-login', project: 'website', after: null, refused: null },
      { position: 2, task: 'login-docs', project: 'website', after: 'fix-login', refused: null },
      { position: 3, task: 'bare', project: 'website', after: null, refused: "'bare' has no done-when" },
    ],
    runs: [
      { task: 'old', project: 'website', run: '20261020-0100-aaaa', state: 'met', reason: 'every done-when met', costUsd: 9, started: '2026-10-20T01:00:00Z' },
      { task: 'cache', project: 'website', run: '20261022-0200-bbbb', state: 'failed', reason: 'over budget', costUsd: 3.5, started: '2026-10-22T02:00:00Z' },
    ],
    settings: { windowUsd: null, windowRuns: null, heldCap: 2, draftPullRequests: false },
    ...over,
  };
}

function setup(over: Partial<QueueRead> = {}, plan: QueuePlan | Error = { order: [], left: [], stops: 'The queue is empty.' }) {
  const acted: [string, string[]][] = [];
  const shown = render(QueuePanel, {
    queue: queue(over),
    projects: ['website', 'homelab'],
    tasksOf: async (project: string) =>
      project === 'website'
        ? [
            { id: 'fix-login', title: 'Fix the login', state: 'open', kind: 'task', doneWhen: ['the suite passes'] },
            { id: 'vague', title: 'Make it better', state: 'open', kind: 'task', doneWhen: [] },
            { id: 'shipped', title: 'Shipped', state: 'done', kind: 'task' },
            { id: 'a-thought', title: 'A thought', state: 'open', kind: 'idea' },
            { id: 'cache', title: 'Cache it', state: 'blocked', kind: 'task' },
          ]
        : [],
    act: (path: string, words: string[]) => {
      acted.push([path, words]);

      return true;
    },
    readPlan: async () => {
      if (plan instanceof Error) {
        throw plan;
      }

      return plan;
    },
    now: () => NOW,
  });

  return { acted, shown };
}

const said = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent ?? '';

describe('QueuePanel', () => {
  it('lists what waits in order, with what would be refused and why', () => {
    setup();

    const list = screen.getByRole('list', { name: "Waiting in builders' queue" });
    const rows = within(list).getAllByRole('listitem');

    expect(rows.map((row) => row.querySelector('b')?.textContent)).toEqual(['1. fix-login', '2. login-docs', '3. bare']);
    expect(rows[1].textContent).toContain('website, after fix-login');
    expect(rows[2].textContent).toContain("Would be refused: 'bare' has no done-when");
    expect(rows[0].textContent).not.toContain('Would be refused');
  });

  it('says what it has run, newest first, and the last day’s spend', () => {
    setup();

    const ran = within(screen.getByRole('list', { name: "Runs builders' queue started" })).getAllByRole('listitem');
    expect(ran[0].textContent).toContain('cache');
    expect(ran[0].textContent).toContain('failed');
    expect(ran[0].textContent).toContain('over budget');
    expect(ran[0].querySelector('a')?.getAttribute('href')).toBe('#teams/20261022-0200-bbbb');

    // Only the run inside the last day counts.
    expect(screen.getByText(/\$3\.50 in the last day/)).toBeTruthy();
  });

  it('shows the draft pull request grant first when it is on, and turns it off', async () => {
    const { acted } = setup({ settings: { windowUsd: '12', windowRuns: 3, heldCap: 2, draftPullRequests: true } });

    expect(screen.getByRole('note').textContent).toContain('draft pull request');
    const stop = screen.getByRole('button', { name: 'Stop that' });
    expect(said(stop)).toBe('$ loadout team queue settings builders --draft-prs off');

    await fireEvent.click(stop);
    expect(acted).toEqual([['team queue settings', ['builders', '--draft-prs', 'off']]]);
    expect(screen.getByText(/\$12 a window · 3 run\(s\) a window · at most 2 held/)).toBeTruthy();
  });

  it('moves an item up or down by its place, and not past either end', async () => {
    const { acted } = setup();
    const rows = within(screen.getByRole('list', { name: "Waiting in builders' queue" })).getAllByRole('listitem');

    expect(said(within(rows[0]).getByRole('button', { name: 'Up' }))).toBe('already first');
    expect(said(within(rows[2]).getByRole('button', { name: 'Down' }))).toBe('already last');
    expect(said(within(rows[1]).getByRole('button', { name: 'Up' }))).toBe('$ loadout team queue move builders login-docs 1');

    await fireEvent.click(within(rows[0]).getByRole('button', { name: 'Up' }));
    await fireEvent.click(within(rows[1]).getByRole('button', { name: 'Up' }));
    await fireEvent.click(within(rows[1]).getByRole('button', { name: 'Down' }));
    await fireEvent.click(within(rows[2]).getByRole('button', { name: 'Down' }));

    expect(acted).toEqual([
      ['team queue move', ['builders', 'login-docs', '1', '--project', 'website']],
      ['team queue move', ['builders', 'login-docs', '3', '--project', 'website']],
    ]);
  });

  it('takes an item out, and starts the next one', async () => {
    const { acted } = setup();
    const rows = within(screen.getByRole('list', { name: "Waiting in builders' queue" })).getAllByRole('listitem');

    await fireEvent.click(within(rows[2]).getByRole('button', { name: 'Take out' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Run the next one' }));

    expect(acted).toEqual([
      ['team queue remove', ['builders', 'bare', '--project', 'website']],
      ['team queue run-next', ['builders']],
    ]);
  });

  it('adds a task picked from a project’s open work, after another when asked', async () => {
    const { acted } = setup();

    await fireEvent.click(screen.getByRole('button', { name: 'Add a task' }));
    const form = screen.getByRole('form', { name: "Add a task to builders' queue" });

    await fireEvent.change(within(form).getByLabelText('Project'), { target: { value: 'website' } });
    await waitFor(() => expect(within(form).getAllByRole('option', { name: /fix-login ·/ })).toHaveLength(1));

    // Ideas and finished work are not offered.
    const offered = [...(within(form).getByLabelText('Task') as HTMLSelectElement).options].map((one) => one.value);
    expect(offered).toEqual(['', 'fix-login', 'vague', 'cache']);

    await fireEvent.change(within(form).getByLabelText('Task'), { target: { value: 'cache' } });
    await fireEvent.change(within(form).getByLabelText('After, if it waits for one'), { target: { value: 'fix-login' } });

    const add = within(form).getByRole('button', { name: 'Add it' });
    expect(said(add)).toBe('$ loadout team queue add builders cache --project website --after fix-login');

    await fireEvent.click(add);
    expect(acted).toEqual([['team queue add', ['builders', 'cache', '--project', 'website', '--after', 'fix-login']]]);
  });

  it('sends only the limits that changed, and none for no cap', async () => {
    const { acted } = setup({ settings: { windowUsd: '12', windowRuns: 3, heldCap: 2, draftPullRequests: false } });

    await fireEvent.click(screen.getByRole('button', { name: 'Change its limits' }));
    const form = screen.getByRole('form', { name: "builders' queue limits" });

    await fireEvent.input(within(form).getByLabelText('Most a window may spend, US dollars'), { target: { value: '' } });
    await fireEvent.input(within(form).getByLabelText('Runs that may wait at a gate at once'), { target: { value: '1' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save them' }));

    expect(acted).toEqual([['team queue settings', ['builders', '--window-usd', 'none', '--held-cap', '1']]]);
  });

  it('shows what would run, and what would be left and why', async () => {
    setup({}, { order: [{ task: 'fix-login', project: 'website' }], left: [{ task: 'bare', project: 'website', reason: 'It has no done-when.' }], stops: 'The queue is empty.' });

    await fireEvent.click(screen.getByRole('button', { name: 'What would run' }));
    const plan = await screen.findByRole('region', { name: "What builders' queue would run" });

    expect(plan.textContent).toContain('fix-login on website');
    expect(plan.textContent).toContain('Left: bare on website. It has no done-when.');
  });

  it('says why when the plan cannot be read', async () => {
    setup({}, new Error('No team named builders.'));

    await fireEvent.click(screen.getByRole('button', { name: 'What would run' }));
    expect(await screen.findByText('No team named builders.')).toBeTruthy();
  });

  it('says when there is no window, and gives it one', async () => {
    const { acted } = setup();

    const note = screen.getByRole('group', { name: "When builders' queue runs" });
    expect(note.textContent).toContain('No window: its runs start only when you press Run the next one.');

    await fireEvent.click(within(note).getByRole('button', { name: 'Give it a window' }));
    const form = screen.getByRole('form', { name: "builders' queue window" });
    await fireEvent.input(within(form).getByLabelText("Opens each day at, this machine's time"), { target: { value: '23:30' } });
    await fireEvent.input(within(form).getByLabelText('Stays open for'), { target: { value: '6h' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save the window' }));

    expect(acted).toEqual([['team queue window', ['builders', '--at', '23:30', '--for', '6h']]]);
  });

  it('shows the window it has, open or not, and takes it away', async () => {
    const { acted } = setup({ window: { at: '22:00', lengthMinutes: 480, autonomy: 'supervised', open: true } });

    const note = screen.getByRole('group', { name: "When builders' queue runs" });
    expect(note.textContent).toContain('Worked through daily from 22:00 for 8h, supervised · open now.');

    await fireEvent.click(within(note).getByRole('button', { name: 'No window' }));

    expect(acted).toEqual([['team queue window', ['builders', '--off']]]);
  });

  it('counts spending from the window it is in, while one is open', () => {
    setup({ windowOpened: '2026-10-22T02:30:00Z' });

    // The 02:00 run started before this window, so only nothing is counted.
    expect(screen.getByRole('region', { name: "builders' queue" }).querySelector('header em')?.textContent).not.toContain('$');

    document.body.innerHTML = '';
    setup({ windowOpened: '2026-10-22T01:30:00Z' });

    expect(screen.getByRole('region', { name: "builders' queue" }).querySelector('header em')?.textContent).toContain('$3.50 in this window');
  });

  it('marks the tasks the queue would refuse for having no done-when', async () => {
    setup();

    await fireEvent.click(screen.getByRole('button', { name: 'Add a task' }));
    const form = screen.getByRole('form', { name: "Add a task to builders' queue" });
    await fireEvent.change(within(form).getByLabelText('Project'), { target: { value: 'website' } });

    await waitFor(() => expect(within(within(form).getByLabelText('Task')).getByRole('option', { name: /vague/ }).textContent).toContain('(no done-when yet)'));
    expect(within(within(form).getByLabelText('Task')).getByRole('option', { name: /fix-login/ }).textContent).not.toContain('no done-when');
  });
});
