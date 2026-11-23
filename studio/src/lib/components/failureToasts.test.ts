// A Problem that says why: the one-line cause on the card, Details behind a
// click, Copy command and Copy details, and Retry only where the call site
// said the action is safe to repeat. The Earlier list is a record, never a
// place to act.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import Toasts from './Toasts.svelte';
import { ToastStack } from '../toasts.svelte';
import { JobFailed, StillGoing, type Job } from '../api';

window.matchMedia ??= ((query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;

function failedJob(error: string, exitCode = 2): JobFailed {
  const job: Job = {
    id: 'j1',
    path: 'task show',
    line: 'task show a',
    state: 'failed',
    exitCode,
    startedAt: '2026-11-23T01:00:00Z',
    endedAt: '2026-11-23T01:00:01Z',
    truncated: false,
    output: '',
    error,
  };

  return new JobFailed(job, error);
}

/** A closure that fails the given number of times, then works. */
function flaky(failures: number) {
  let left = failures;
  const run = vi.fn(async () => {
    if (left-- > 0) {
      throw failedJob(`Error: attempt failed, ${left} to go\n`);
    }
  });

  return run;
}

function deferred() {
  let settle!: (fail?: unknown) => void;
  const promise = new Promise<void>((resolve, reject) => {
    settle = (fail?: unknown) => (fail ? reject(fail) : resolve());
  });

  return { promise, settle };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('the stack', () => {
  it('raises a Problem with the one-line cause and keeps the rest for Details', () => {
    const stack = new ToastStack();
    const id = stack.fail(failedJob('Fetching\nError: the project is not registered\n'), { cmd: 'loadout task show a' });
    const toast = stack.list.find((one) => one.id === id);

    expect(toast?.level).toBe('problem');
    expect(toast?.text).toBe('Error: the project is not registered');
    expect(toast?.failure?.exitCode).toBe(2);
    expect(toast?.failure?.output).toContain('Fetching');
    expect(toast?.retry).toBeUndefined();
  });

  it('puts the call site\'s words before the cause', () => {
    const stack = new ToastStack();
    stack.fail(new Error('the daemon refused'), { lead: 'Two were not recorded.' });

    expect(stack.list[0].text).toBe('Two were not recorded. the daemon refused');
  });

  it('gives a command that is still going its own notice, with no Retry', () => {
    const stack = new ToastStack();
    stack.fail(new StillGoing("'loadout x' is still going."), { retry: async () => {} });

    expect(stack.list[0].level).toBe('info');
    expect(stack.list[0].retry).toBeUndefined();
    expect(stack.list[0].failure).toBeUndefined();
  });

  it('updates the same toast when a Retry fails again, counting the attempts and keeping each cause', async () => {
    const stack = new ToastStack();
    const run = flaky(5);
    const id = stack.fail(failedJob('Error: first\n'), { retry: run });

    await stack.retry(id);
    await stack.retry(id);

    expect(stack.list).toHaveLength(1);
    const toast = stack.list[0];
    expect(toast.id).toBe(id);
    expect(toast.level).toBe('problem');
    expect(toast.attempts).toBe(3);
    expect(toast.text).toBe('Error: attempt failed, 3 to go');
    expect(toast.failure?.earlier).toEqual(['Error: first', 'Error: attempt failed, 4 to go']);
    expect(stack.alert).toBe('Problem, attempt 3: Error: attempt failed, 3 to go');
  });

  it('turns into a Done when a Retry works, which the history keeps with the attempts', async () => {
    const stack = new ToastStack();
    const id = stack.fail(failedJob('Error: first\n'), { retry: flaky(1) });

    await stack.retry(id);
    await stack.retry(id);

    expect(stack.list[0].text).toBe('Worked on attempt 3');
    expect(stack.counts.done).toBe(1);
    expect(stack.list[0].retry).toBeUndefined();

    for (let second = 0; second < 10; second += 1) {
      stack.tick();
    }

    expect(stack.list).toHaveLength(0);
    expect(stack.history[0]).toMatchObject({ id, level: 'done', text: 'Worked on attempt 3', attempts: 3 });
    expect(stack.history[0].failure?.earlier).toEqual(['Error: first', 'Error: attempt failed, 0 to go']);
  });

  it('runs one attempt at a time', async () => {
    const stack = new ToastStack();
    const waiting = deferred();
    const run = vi.fn(() => waiting.promise);
    const id = stack.fail(new Error('x'), { retry: run });

    const first = stack.retry(id);
    expect(stack.list[0].retrying).toBe(true);
    void stack.retry(id);
    expect(run).toHaveBeenCalledTimes(1);

    waiting.settle();
    await first;
    expect(stack.list[0].retrying).toBe(false);
  });

  it('lets a toast close while a Retry runs, and then only rewrites its record', async () => {
    const stack = new ToastStack();
    const waiting = deferred();
    const id = stack.fail(new Error('first'), { retry: () => waiting.promise });

    const attempt = stack.retry(id);
    stack.dismiss(id);

    expect(stack.list).toHaveLength(0);
    expect(stack.history[0]).toMatchObject({ id, level: 'problem', retrying: true });

    waiting.settle();
    await attempt;

    expect(stack.list).toHaveLength(0);
    expect(stack.history[0]).toMatchObject({ id, level: 'done', text: 'Worked on attempt 2', retrying: false, attempts: 2 });
  });

  it('records a failed attempt after closing as a Problem with the new cause', async () => {
    const stack = new ToastStack();
    const waiting = deferred();
    const id = stack.fail(new Error('first'), { retry: () => waiting.promise });

    const attempt = stack.retry(id);
    stack.dismiss(id);
    waiting.settle(new Error('second'));
    await attempt;

    expect(stack.history[0]).toMatchObject({ id, level: 'problem', text: 'second', retrying: false, attempts: 2 });
  });

  it('keeps a Problem a Problem in the history when it was acknowledged rather than fixed', () => {
    const stack = new ToastStack();
    const id = stack.fail(new Error('it broke'));

    stack.settle(id, 'Seen.');
    stack.dismiss(id);

    expect(stack.history[0].level).toBe('problem');
  });

  it('keeps no closures in the history', () => {
    const stack = new ToastStack();
    const id = stack.fail(new Error('x'), { retry: async () => {} });
    stack.dismiss(id);

    expect(Object.values(stack.history[0]).some((value) => typeof value === 'function')).toBe(false);
  });
});

describe('the card', () => {
  it('shows the cause, and Details shows the code, the command and the output', async () => {
    const stack = new ToastStack();
    stack.fail(failedJob('Fetching\nError: the project is not registered\n'), { cmd: 'loadout task show a' });
    render(Toasts, { stack, clock: false });

    const card = screen.getByRole('group', { name: 'Problem' });
    expect(within(card).getByText('Error: the project is not registered')).toBeTruthy();
    expect(card.querySelector('.why')).toBeNull();

    await fireEvent.click(within(card).getByRole('button', { name: 'Details' }));

    const why = card.querySelector('.why');
    expect(why?.textContent).toContain('Exit code2');
    expect(why?.textContent).toContain('loadout task show a');
    expect(why?.querySelector('.out')?.textContent).toContain('Fetching');
  });

  it('offers Retry only where the call site marked the action repeatable', () => {
    const stack = new ToastStack();
    stack.fail(new Error('not marked'));
    stack.fail(new Error('marked'), { retry: async () => {} });
    render(Toasts, { stack, clock: false });

    const [marked, unmarked] = screen.getAllByRole('group', { name: 'Problem' });
    expect(within(marked).queryByRole('button', { name: /Retry/ })).toBeTruthy();
    expect(within(unmarked).queryByRole('button', { name: /Retry/ })).toBeNull();
  });

  it('holds Retry while an attempt runs', async () => {
    const stack = new ToastStack();
    const waiting = deferred();
    stack.fail(new Error('x'), { retry: () => waiting.promise });
    render(Toasts, { stack, clock: false });

    await fireEvent.click(screen.getByRole('button', { name: /^Retry/ }));
    await tick();

    const held = screen.getByRole('button', { name: /Retrying…/ });
    expect(held.getAttribute('aria-disabled')).toBe('true');

    waiting.settle(new Error('y'));
    await tick();
    await tick();
    expect(screen.getByRole('button', { name: /^Retry/ }).getAttribute('aria-disabled')).toBeNull();
  });

  it('copies a command with its secret redacted, and says so', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const stack = new ToastStack();
    stack.fail(new Error('refused'), { cmd: 'loadout login --token abc123' });
    render(Toasts, { stack, clock: false });

    await fireEvent.click(screen.getByRole('button', { name: /Copy command/ }));
    await tick();

    expect(writeText).toHaveBeenCalledWith('loadout login --token [redacted]');
    expect(document.querySelector('.copied')?.textContent).toBe('Copied the command with its secret redacted: fill in [redacted] before you run it.');
  });

  it('copies the details redacted', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const stack = new ToastStack();
    stack.fail(failedJob('PASSWORD=hunter2\nError: login failed\n'), { cmd: 'loadout x' });
    render(Toasts, { stack, clock: false });

    await fireEvent.click(screen.getByRole('button', { name: /Copy details/ }));

    const copied = (writeText.mock.calls[0] as unknown as [string])[0];
    expect(copied).toContain('Cause: Error: login failed');
    expect(copied).toContain('PASSWORD=[redacted]');
    expect(copied).not.toContain('hunter2');
  });
});

describe('the Earlier list', () => {
  it('never offers Retry, and notes the attempts a fixed failure took', async () => {
    const stack = new ToastStack();
    const fixed = stack.fail(new Error('first'), { retry: flaky(1) });
    stack.fail(new Error('still broken'), { retry: async () => { throw new Error('again'); } });
    await stack.retry(fixed);
    await stack.retry(fixed);
    stack.dismiss(stack.list[1].id);
    stack.dismiss(stack.list[0].id);
    render(Toasts, { stack, clock: false });

    await fireEvent.click(screen.getByRole('button', { name: /Earlier/ }));
    const earlier = screen.getByRole('group', { name: 'Earlier notifications, this session' });

    expect(within(earlier).queryByRole('button', { name: /Retry/ })).toBeNull();
    const rows = earlier.querySelectorAll('li');
    const done = Array.from(rows).find((row) => row.classList.contains('done'));
    expect(done?.textContent).toContain('Worked on attempt 3');
    expect(done?.textContent).toContain('after 3 attempts');
    expect(Array.from(rows).find((row) => row.classList.contains('problem'))?.textContent).toContain('still broken');
  });

  it('opens a record\'s Details apart from the card it came from', async () => {
    const stack = new ToastStack();
    const id = stack.fail(failedJob('Error: kept\n'));
    stack.dismiss(id);
    render(Toasts, { stack, clock: false });

    await fireEvent.click(screen.getByRole('button', { name: /Earlier/ }));
    const earlier = screen.getByRole('group', { name: 'Earlier notifications, this session' });
    await fireEvent.click(within(earlier).getByRole('button', { name: 'Details' }));

    expect(earlier.querySelector('.why .out')?.textContent).toContain('Error: kept');
  });
});
