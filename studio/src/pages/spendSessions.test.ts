// Spend's recent sessions: two sessions may share an id when they belong to
// different agents or folders, and the list must still draw both. One
// clashing key once stopped the whole list from drawing.
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();
  const sessions = [
    { agent: 'claude', id: 'same-id', title: 'In starstats', directory: 'C:/example/src/starstats', branch: 'main', lastActive: '2026-10-18T10:00:00Z' },
    { agent: 'claude', id: 'same-id', title: 'In storefront', directory: 'C:/example/src/storefront', branch: 'main', lastActive: '2026-10-18T11:00:00Z' },
  ];

  return {
    ...real,
    readJson: vi.fn(async (path: string) => (path === 'sessions' ? sessions : new Promise(() => undefined))),
  };
});

const { default: Spend } = await import('./Spend.svelte');

describe('Spend', () => {
  it('lists two sessions that share an id', async () => {
    render(Spend, {});

    await vi.waitFor(() => expect(screen.getByText('In starstats')).toBeTruthy());
    expect(screen.getByText('In storefront')).toBeTruthy();
  });
});
