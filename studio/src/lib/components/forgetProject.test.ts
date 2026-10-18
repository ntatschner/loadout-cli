// ForgetProject: what runs, in order, for each choice; what cannot be
// ticked and why; a preview that changes nothing; a second press to forget
// a project everywhere; and a run that stops at the first failure.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import ForgetProject from './ForgetProject.svelte';
import { forgetSteps, runSteps, type ForgetScope, type Step } from '../forget';

const COUNTS = { tasks: ['t1', 't2'], ideas: ['i1'], topics: ['release-process'] };
const plan = () => [...document.querySelectorAll('ol.plan li')].map((one) => one.textContent);

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await Promise.resolve();
  }
}

describe('ForgetProject', () => {
  afterEach(() => vi.useRealTimers());

  it('on this machine only runs one line, without --from-workspace', () => {
    render(ForgetProject, { project: { id: 'starstats', name: 'StarStats', available: true }, counts: COUNTS });

    expect(plan()).toEqual(['$ loadout project remove starstats']);
  });

  it('never removes tasks, ideas or topics when forgetting on this machine only', () => {
    const all = { protect: true, tasks: true, ideas: true, topics: true };

    expect(forgetSteps('starstats', 'here', all, COUNTS, true).map((one) => one.line)).toEqual(['$ loadout protect starstats --remove', '$ loadout project remove starstats']);
  });

  it('on this machine only, the workspace cleanups cannot be ticked and say why', () => {
    render(ForgetProject, { project: { id: 'starstats', available: true }, counts: COUNTS });

    for (const title of ['Its 2 tasks', 'Its 1 idea', 'Its 1 memory topic']) {
      const box = screen.getByRole('checkbox', { name: new RegExp(title) }) as HTMLInputElement;

      expect(box.disabled).toBe(true);
      expect(box.closest('label')?.textContent).toContain('the workspace keeps them for your other machines');
    }

    expect((screen.getByRole('checkbox', { name: /Git protections/ }) as HTMLInputElement).disabled).toBe(false);
  });

  it('from the workspace, each ticked cleanup adds a line per item, before project remove', async () => {
    render(ForgetProject, { project: { id: 'starstats', available: true }, counts: COUNTS });

    await fireEvent.click(screen.getByRole('radio', { name: /From the Loadout workspace/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: /Git protections/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: /Its 2 tasks/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: /Its 1 memory topic/ }));

    expect(plan()).toEqual([
      '$ loadout protect starstats --remove',
      '$ loadout task remove t1 --project starstats',
      '$ loadout task remove t2 --project starstats',
      '$ loadout memory delete release-process starstats --yes',
      '$ loadout project remove starstats --from-workspace',
    ]);
  });

  it('while what it has is still being read, says so rather than that it has none', async () => {
    render(ForgetProject, { project: { id: 'starstats', available: true }, counts: { tasks: null, ideas: [], topics: null } });

    await fireEvent.click(screen.getByRole('radio', { name: /From the Loadout workspace/ }));

    const tasks = screen.getByRole('checkbox', { name: /Its tasks/ }) as HTMLInputElement;
    expect(tasks.disabled).toBe(true);
    expect(tasks.closest('label')?.textContent).toContain('Still reading what it has.');
    expect(screen.getByRole('checkbox', { name: /Its 0 ideas/ }).closest('label')?.textContent).toContain('It has none.');
  });

  it('does not offer the Git protections for a project that is not on this machine', () => {
    render(ForgetProject, { project: { id: 'home-lab', available: false }, counts: COUNTS });

    const box = screen.getByRole('checkbox', { name: /Git protections/ }) as HTMLInputElement;
    expect(box.disabled).toBe(true);
    expect(box.closest('label')?.textContent).toContain('It is not on this machine');
    expect(forgetSteps('home-lab', 'everywhere', { protect: true, tasks: false, ideas: false, topics: false }, COUNTS, false).map((one) => one.path)).toEqual(['project remove']);
  });

  it('previews every line with --dry-run', async () => {
    const runs: { steps: Step[]; preview: boolean }[] = [];
    render(ForgetProject, { project: { id: 'starstats', available: true }, counts: COUNTS, onrun: async (one: { scope: ForgetScope; steps: Step[]; preview: boolean }) => (runs.push(one), 'ok') });

    await fireEvent.click(screen.getByRole('radio', { name: /From the Loadout workspace/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: /Its 1 idea/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
    await settle();

    expect(runs).toHaveLength(1);
    expect(runs[0].preview).toBe(true);
    expect(runs[0].steps.map((one) => one.line)).toEqual(['$ loadout idea remove i1 --project starstats --dry-run', '$ loadout project remove starstats --from-workspace --dry-run']);
  });

  it('asks for a second press within five seconds to forget it on every machine', async () => {
    vi.useFakeTimers();
    const runs: { scope: ForgetScope }[] = [];
    render(ForgetProject, { project: { id: 'starstats', available: true }, counts: COUNTS, onrun: async (one: { scope: ForgetScope; steps: Step[]; preview: boolean }) => (runs.push(one), 'ok') });

    await fireEvent.click(screen.getByRole('radio', { name: /From the Loadout workspace/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Forget it on every machine' }));
    expect(runs).toHaveLength(0);
    expect(screen.getByText(/Press the button again within five seconds/)).toBeTruthy();

    // Too late: the arming lapses after five seconds.
    await vi.advanceTimersByTimeAsync(5001);
    await fireEvent.click(screen.getByRole('button', { name: 'Forget it on every machine' }));
    expect(runs).toHaveLength(0);

    await fireEvent.click(screen.getByRole('button', { name: 'Press again to forget it everywhere' }));
    await vi.advanceTimersByTimeAsync(0);
    expect(runs.map((one) => one.scope)).toEqual(['everywhere']);
  });

  it('stops at the first line that fails', async () => {
    const steps = forgetSteps('starstats', 'everywhere', { protect: false, tasks: true, ideas: true, topics: false }, COUNTS, true);
    const ran: string[] = [];

    const outcome = await runSteps(steps, async (one) => (ran.push(one.line), !one.line.includes('t2')));

    expect(ran).toEqual(['$ loadout task remove t1 --project starstats', '$ loadout task remove t2 --project starstats']);
    expect(outcome.ran).toBe(1);
    expect(outcome.failed?.line).toBe('$ loadout task remove t2 --project starstats');
  });
});
