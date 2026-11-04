// The "Before this" band: one key takes the first offer, by the command shown,
// and nothing is offered for a check nobody could look at.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { OfferSheet } from '../offers';

let sheet: OfferSheet;

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();

  return { ...real, readJson: vi.fn(async () => sheet) };
});

const { default: BeforeThis } = await import('./BeforeThis.svelte');
const { studio } = await import('../state.svelte');

const ci = {
  check: 'ci',
  name: 'CI on push and pull request',
  state: 'gap',
  evidence: 'No workflow runs on push or pull request.',
  canOffer: true,
  stacks: ['all', 'dotnet', 'node', 'generic'],
  environments: [],
  prerequisite: null,
  linked: false,
};

const blank = (): OfferSheet => ({ project: 'p', task: 'ship', built: '', github: 'not asked', offers: [ci], unseen: [], notNeeded: [], notes: [] });

async function settle() {
  for (let i = 0; i < 10; i++) {
    await tick();
    await Promise.resolve();
  }
}

async function band(disabledReason?: string) {
  const run = vi.fn(async () => true);

  render(BeforeThis, { id: 'ship', project: 'p', revision: '1', disabledReason, run });
  await settle();

  return run;
}

describe('the band', () => {
  beforeEach(() => {
    sheet = blank();
    studio.keys = true;
  });

  afterEach(cleanup);

  it('takes the first offer with one key, with the default stack left out of the words', async () => {
    const run = await band();

    await fireEvent.keyDown(document.body, { key: 'o' });
    await settle();

    expect(run).toHaveBeenCalledTimes(1);
    expect(run.mock.calls[0].slice(0, 2)).toEqual(['task offers', ['ship', '--project', 'p', '--accept=ci']]);
  });

  it('changes the stack with another key, and the words then name it', async () => {
    const run = await band();

    await fireEvent.keyDown(document.body, { key: 's' });
    await settle();
    expect(screen.getByText(/For dotnet\./)).toBeTruthy();

    await fireEvent.keyDown(document.body, { key: 'o' });
    await settle();

    expect((run.mock.calls[0] as unknown[])[1]).toEqual(['ship', '--project', 'p', '--accept=ci', '--stack=dotnet']);
  });

  it('leaves the key alone while somebody is typing', async () => {
    const run = await band();
    const typing = document.body.appendChild(document.createElement('input'));

    await fireEvent.keyDown(typing, { key: 'o' });
    await settle();
    typing.remove();

    expect(run).not.toHaveBeenCalled();
  });

  it('takes nothing while only watching', async () => {
    const run = await band('Studio is only watching');

    await fireEvent.keyDown(document.body, { key: 'o' });
    await settle();

    expect(run).not.toHaveBeenCalled();
  });

  it('says a check could not be looked at, and offers nothing for it', async () => {
    sheet = { ...blank(), offers: [], unseen: [{ check: 'environments', state: 'unknown', hint: 'No GitHub token.' }] };

    const run = await band();

    expect(screen.getByText(/could not be looked at/)).toBeTruthy();
    expect(screen.queryByText('Add it first')).toBeNull();

    await fireEvent.keyDown(document.body, { key: 'o' });
    await settle();

    expect(run).not.toHaveBeenCalled();
  });

  it('shows nothing at all with nothing missing', async () => {
    sheet = { ...blank(), offers: [] };

    await band();

    expect(screen.queryByText('Before this')).toBeNull();
  });

  it('says a link the type has outgrown even with nothing else to show, and takes it off by the command shown', async () => {
    sheet = { ...blank(), offers: [], links: [{ check: 'ci', prerequisite: 'add-ci', outgrown: true }] };

    const run = await band();

    expect(screen.getByText(/still waits for add-ci/)).toBeTruthy();

    await fireEvent.click(screen.getByText('Stop waiting for it'));
    await settle();

    expect((run.mock.calls[0] as unknown[]).slice(0, 2)).toEqual(['task requires', ['ship', 'add-ci', '--project', 'p', '--remove']]);
  });

  it('tells the page how many links there are, for the box beside the type picker', async () => {
    sheet = { ...blank(), offers: [{ ...ci, prerequisite: 'add-ci', linked: true }], links: [{ check: 'ci', prerequisite: 'add-ci', outgrown: false }] };

    const onlinks = vi.fn();
    const run = vi.fn(async () => true);

    render(BeforeThis, { id: 'ship', project: 'p', revision: '1', run, onlinks });
    await settle();

    expect(onlinks).toHaveBeenCalledWith(1);
    expect(screen.queryByText(/no longer depends/)).toBeNull();
  });

  it('offers to stop waiting once linked, and no key takes it again', async () => {
    sheet = { ...blank(), offers: [{ ...ci, prerequisite: 'add-ci', linked: true }] };

    const run = await band();

    expect(screen.getByText('Stop waiting for it')).toBeTruthy();

    await fireEvent.keyDown(document.body, { key: 'o' });
    await settle();

    expect(run).not.toHaveBeenCalled();
  });
});
