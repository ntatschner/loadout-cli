// ForgetDialog reads what a project has once per project. The dossier reads
// the project again after every job and hands the dialog a fresh copy;
// following that read the counts again mid-run and dropped the file count
// project remove had reported.
import { describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/svelte';
import { tick } from 'svelte';

const reads: string[] = [];

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string) => {
      reads.push(path);

      return path === 'task list' ? { tasks: [] } : path === 'idea list' ? { ideas: [] } : { localPath: '/w' };
    }),
    memory: { ...real.memory, topics: vi.fn(async () => (reads.push('topics'), { project: 'p', topics: [], agent: null })) },
  };
});

const { default: ForgetDialog } = await import('./ForgetDialog.svelte');

describe('ForgetDialog', () => {
  it('reads once for a project, however often it is handed a fresh copy of it', async () => {
    const shown = render(ForgetDialog, { project: { id: 'studio-check', name: 'Studio check', available: true }, onclose: () => undefined });
    await tick();
    const first = reads.length;

    await shown.rerender({ project: { id: 'studio-check', name: 'Studio check', available: true }, onclose: () => undefined });
    await tick();

    expect(first).toBe(4);
    expect(reads.length).toBe(first);

    await shown.rerender({ project: { id: 'another', name: 'Another', available: true }, onclose: () => undefined });
    await tick();
    expect(reads.length).toBe(first * 2);
  });
});
