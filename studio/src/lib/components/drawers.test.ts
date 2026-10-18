// ActionDrawer (CommandAction), TerminalOnly and HealthList: the effect a
// drawer wears is the coverage list's, what its button says follows what the
// line will do, and findings read worst first whatever case they came in.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job, StudioCommand } from '../api';

const ran: { path: string; words: string[] }[] = [];
const opened: { command?: string; words?: string[] }[] = [];

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();
  const job: Job = { id: 'j1', path: 'x', line: '', state: 'done', exitCode: 0, startedAt: '', endedAt: '', truncated: false, output: '', error: '' };

  return {
    ...real,
    commands: {
      ...real.commands,
      catalogue: vi.fn(async () => []),
      run: vi.fn(async (path: string, words: string[]) => {
        ran.push({ path, words });

        return job;
      }),
      job: vi.fn(async () => job),
      jobs: vi.fn(async () => [job]),
    },
    sessions: {
      ...real.sessions,
      start: vi.fn(async (launch: { command?: string; words?: string[] }) => {
        opened.push(launch);

        return { id: 's1' };
      }),
    },
  };
});

const { default: CommandAction } = await import('./CommandAction.svelte');
const { default: HealthList } = await import('./HealthList.svelte');
const { studio } = await import('../state.svelte');
const { severityOf, worstFirst } = await import('../health');

const flag = (name: string) => ({ name, short: null, kind: 'flag' as const, valueName: null, description: '', required: false, global: false, choices: [] });

function command(path: string, effect: StudioCommand['reach']['effect'], extra: Partial<StudioCommand> = {}): StudioCommand {
  return {
    path,
    description: `What ${path} does.`,
    category: 'Test',
    example: '',
    runnable: true,
    reach: { page: 'settings', effect, reason: effect === 'terminalOnly' ? 'asks for the value at a masked prompt' : null, changesOnlyWith: null },
    arguments: [],
    options: [],
    ...extra,
  };
}

const CATALOGUE: StudioCommand[] = [
  command('doctor', 'reads'),
  command('memory reindex', 'changes', { options: [flag('--dry-run')] }),
  command('project onboard', 'session'),
  command('secret set', 'terminalOnly'),
  command('backup restore', 'changes', { reach: { page: 'settings', effect: 'changes', reason: null, changesOnlyWith: ['--apply'] }, options: [flag('--apply')] }),
  command('task remove', 'changes', { arguments: [{ name: 'ID', position: 0, required: true, many: false, description: '' }] }),
];

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await Promise.resolve();
  }
}

/** The drawer for a path, opened. */
async function drawer(path: string, title = path) {
  const shown = render(CommandAction, { path, title });

  await fireEvent.click(screen.getByRole('button', { name: new RegExp(title) }));
  await settle();

  return within(shown.container.querySelector('.ls-drawer') as HTMLElement);
}

/** What a halo button says under it: its command, or why it is disabled. */
const said = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent ?? '';

describe('ActionDrawer', () => {
  beforeEach(() => {
    studio.catalogue = CATALOGUE;
    studio.about = null;
  });

  afterEach(() => {
    ran.length = 0;
    opened.length = 0;
  });

  it('wears the effect the coverage list gives each command', async () => {
    for (const [path, word] of [
      ['doctor', 'reads only'],
      ['memory reindex', 'changes something'],
      ['project onboard', 'starts a session'],
      ['secret set', 'terminal only'],
    ]) {
      const shown = render(CommandAction, { path, title: `t ${path}` });

      expect(shown.container.querySelector('.ls-drawer .top .ls-tag')?.textContent).toBe(word);
      shown.unmount();
    }
  });

  it('reads until the option that makes it act is ticked, and its button says so', async () => {
    const box = await drawer('backup restore');
    const run = box.getByRole('button', { name: 'Run' });

    expect(said(run)).toBe('reads only · changes with --apply');
    expect(run.classList.contains('solid')).toBe(false);

    await fireEvent.click(box.getByRole('checkbox'));
    await settle();

    const now = box.getByRole('button', { name: 'Run' });
    expect(said(now)).toBe('changes something');
    expect(now.classList.contains('solid')).toBe(true);
  });

  it('previews with --dry-run', async () => {
    const box = await drawer('memory reindex');

    await fireEvent.click(box.getByRole('button', { name: 'Preview' }));
    await settle();

    expect(ran).toEqual([{ path: 'memory reindex', words: ['--dry-run'] }]);
  });

  it('opens a command that asks as it goes in a terminal', async () => {
    const box = await drawer('project onboard');

    expect(box.queryByRole('button', { name: 'Run' })).toBeNull();
    await fireEvent.click(box.getByRole('button', { name: 'Open it in a terminal' }));
    await settle();

    expect(opened).toEqual([expect.objectContaining({ command: 'project onboard', words: [] })]);
  });

  it('offers no button that runs a terminal-only command, only its line and why', async () => {
    const box = await drawer('secret set');

    expect(box.queryByRole('button', { name: 'Run' })).toBeNull();
    expect(box.getByRole('button', { name: 'Copy' })).toBeTruthy();
    expect(box.getByText('It asks for the value at a masked prompt.')).toBeTruthy();
    expect(box.getByText('$ loadout secret set')).toBeTruthy();
  });

  it('when only watching, refuses a change but still runs a read', async () => {
    studio.about = { watchOnly: true } as typeof studio.about;

    const change = await drawer('memory reindex');
    const button = change.getByRole('button', { name: 'Run' });
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.classList.contains('solid')).toBe(false);
    expect(change.getByRole('button', { name: 'Preview' }).getAttribute('aria-disabled')).toBeNull();

    const read = await drawer('doctor');
    expect(read.getByRole('button', { name: 'Run' }).getAttribute('aria-disabled')).toBeNull();
  });

  it('will not run without a required field, and says which', async () => {
    const box = await drawer('task remove');
    const button = box.getByRole('button', { name: 'Run' });

    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(said(button)).toBe('task remove needs its id.');

    await fireEvent.input(box.getByLabelText(/^id/), { target: { value: 'flaky-tests' } });
    await settle();
    await fireEvent.click(box.getByRole('button', { name: 'Run' }));
    await settle();

    expect(ran).toEqual([{ path: 'task remove', words: ['flaky-tests'] }]);
  });
});

describe('HealthList', () => {
  it('maps severities case-insensitively, worst first', () => {
    expect(['Error', 'WARNING', 'Info', 'ok', 'fine', 'odd'].map(severityOf)).toEqual(['error', 'warning', 'info', 'ok', 'ok', 'info']);
    expect(worstFirst([{ name: 'a', severity: 'Info' }, { name: 'b', severity: 'error' }, { name: 'c', severity: 'Warning' }, { name: 'd', severity: 'ERROR' }]).map((one) => one.name)).toEqual(['b', 'd', 'c', 'a']);
  });

  it('shows each finding as a word and a glyph, and says what can be put right', () => {
    const shown = render(HealthList, {
      findings: [
        { name: 'AI tooling files', severity: 'Info', detail: 'none tracked' },
        { name: 'pre-commit hook', severity: 'Error', detail: 'Missing', fixable: true },
        { name: 'AGENTS.md', severity: 'Warning', detail: 'Changed', fixable: true },
      ],
      from: '$ loadout drift loadout-cli',
    });

    const rows = [...shown.container.querySelectorAll('li')];
    expect(rows.map((row) => row.className)).toEqual(['error', 'warning', 'info']);
    expect(rows[0].querySelector('.n span')?.textContent).toBe('error');
    expect(rows[0].querySelector('svg')).toBeTruthy();
    expect(screen.getByText('1 error and 1 warning; 2 can be put right.')).toBeTruthy();
    expect(screen.getAllByText('can be put right')).toHaveLength(2);
  });

  it('says so in Good when there is nothing', () => {
    render(HealthList, { findings: [] });

    expect(screen.getByText('✓ Nothing to put right.')).toBeTruthy();
  });
});
