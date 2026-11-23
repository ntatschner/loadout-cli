// The plan, read: a written plan says where it is, an edited one says so, a
// file that has gone says so plainly rather than showing a blank, and the
// task's detail and a run for that task each open the same 'idea plan' read.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import PlanViewer from './PlanViewer.svelte';
import TaskDetail from './TaskDetail.svelte';
import type { PlanRead } from '../plans';

const reads: [string, string[]][] = [];
let plan: PlanRead;

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[]) => {
      reads.push([path, words]);

      if (path === 'task show') {
        return {
          project: 'homelab',
          revision: 'r1',
          task: { id: 'status-page', title: 'Status page', state: 'open', history: [] },
          idea: { plan: 'projects/homelab/ideas/status-page.md', accepted: '2026-11-20T10:00:00Z', by: 'nigel' },
        };
      }

      return plan;
    }),
  };
});

describe('The plan, read', () => {
  it('shows a written plan and where it is, with no Confirm', async () => {
    plan = { id: 'status-page', state: 'written', markdown: '# Status page\n', path: 'projects/homelab/ideas/status-page.md', hash: 'h', changed: false };
    render(PlanViewer, { id: 'status-page', where: ['--project', 'homelab'], onclose: () => undefined });

    await vi.waitFor(() => expect(screen.getByRole('heading', { name: 'Status page', level: 1 })).toBeTruthy());
    expect(screen.getByText(/The plan at projects\/homelab\/ideas\/status-page.md, as acceptance wrote it/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Confirm and accept' })).toBeNull();
  });

  it('says plainly when the written file has been edited', async () => {
    plan = { id: 'status-page', state: 'written', markdown: '# Status page\n', path: 'projects/homelab/ideas/status-page.md', hash: 'h', changed: true };
    render(PlanViewer, { id: 'status-page', where: ['--project', 'homelab'], onclose: () => undefined });

    await vi.waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/edited since it was accepted/));
  });

  it('says plainly when the written file has gone', async () => {
    plan = { id: 'status-page', state: 'missing', path: 'projects/homelab/ideas/status-page.md' };
    render(PlanViewer, { id: 'status-page', where: ['--project', 'homelab'], onclose: () => undefined });

    await vi.waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/its plan is not at projects\/homelab\/ideas\/status-page.md any more/));
  });

  it("opens from the task's detail with 'idea plan' on the task's own list", async () => {
    reads.length = 0;
    plan = { id: 'status-page', state: 'written', markdown: '# The written plan\n', path: 'projects/homelab/ideas/status-page.md', hash: 'h', changed: false };
    render(TaskDetail, { task: { id: 'status-page', title: 'Status page', state: 'open', project: 'homelab' }, onclose: () => undefined });

    await vi.waitFor(() => expect(screen.getByRole('button', { name: 'View plan' })).toBeTruthy());
    await fireEvent.click(screen.getByRole('button', { name: 'View plan' }));

    await vi.waitFor(() => expect(screen.getByRole('heading', { name: 'The written plan', level: 1 })).toBeTruthy());
    expect(reads.filter(([path]) => path === 'idea plan')).toEqual([['idea plan', ['status-page', '--project', 'homelab']]]);
  });
});
