// The Events page's filter, as the address holds it, and how the list is
// cut and labelled. The filter string is the same one 'loadout events
// --filter' and the daemon read, so its spelling is pinned here as it is
// in the C# tests.
import { describe, expect, it } from 'vitest';
import type { FeedItem } from './api';
import { byDay, formatFilter, linkOf, outstanding, parseFilter, resolvedAfter } from './events';
import { parse } from './router.svelte';

const item = (over: Partial<FeedItem['event']> = {}, cleared: string | null = null, read = false): FeedItem => ({
  event: { id: 'e', at: '2026-10-21T10:00:00Z', level: 'needs', source: 'run', direction: 'to-me', kind: 'asking', title: 'docs asks: may I?', ...over },
  cleared,
  read,
});

describe('the filter in the address', () => {
  it('writes the same canonical string the command and the daemon write', () => {
    const filter = parseFilter('');

    expect(
      formatFilter({ ...filter, levels: ['info', 'needs'], project: 'loadout cli', sources: ['run'], direction: 'any', since: '1d', unread: true, text: 'merge' }),
    ).toBe('level=needs%2Cinfo&project=loadout%20cli&source=run&direction=any&since=1d&unread=true&text=merge');
  });

  it('reads back what it wrote', () => {
    for (const canonical of ['', 'level=needs', 'source=session%2Cnotice&direction=by-me&since=30m', 'since=2026-10-01&until=2026-10-20&unread=true']) {
      expect(formatFilter(parseFilter(canonical))).toBe(canonical);
    }
  });

  it('leaves out the defaults, so nothing chosen is the empty string', () => {
    expect(formatFilter(parseFilter('direction=to-me&unread=false'))).toBe('');
  });

  it('drops what it does not know rather than refusing, leaving the daemon to say', () => {
    expect(parseFilter('level=urgent,needs&direction=sideways').levels).toEqual(['needs']);
    expect(parseFilter('direction=sideways').direction).toBe('to-me');
  });

  it('is carried after a question mark, leaving the page and its parts alone', () => {
    expect(parse('#events?level=needs&since=1d')).toEqual({ page: 'events', rest: [], query: 'level=needs&since=1d' });
    expect(parse('#teams/run-1')).toEqual({ page: 'teams', rest: ['run-1'], query: '' });
  });
});

describe('the list', () => {
  it('is cut into days, newest first, today and yesterday by name', () => {
    const now = new Date(2026, 9, 21, 15, 0);
    const days = byDay(
      [
        item({ id: 'a', at: new Date(2026, 9, 21, 14, 0).toISOString() }),
        item({ id: 'b', at: new Date(2026, 9, 21, 9, 0).toISOString() }),
        item({ id: 'c', at: new Date(2026, 9, 20, 23, 0).toISOString() }),
        item({ id: 'd', at: new Date(2026, 9, 18, 12, 0).toISOString() }),
      ],
      now,
    );

    expect(days.map((one) => one.day).slice(0, 2)).toEqual(['Today', 'Yesterday']);
    expect(days.map((one) => one.items.map((each) => each.event.id))).toEqual([['a', 'b'], ['c'], ['d']]);
  });

  it('says how long a reason stayed true', () => {
    expect(resolvedAfter(item({}, '2026-10-21T10:04:00Z'))).toBe('resolved 4m later');
    expect(resolvedAfter(item({}, '2026-10-21T12:30:00Z'))).toBe('resolved 2h 30m later');
    expect(resolvedAfter(item())).toBeNull();
  });

  it('calls a reason that has not cleared still open, and a moment never', () => {
    expect(outstanding(item())).toBe(true);
    expect(outstanding(item({}, '2026-10-21T10:04:00Z'))).toBe(false);
    expect(outstanding(item({ kind: 'run-finished', level: 'problem' }))).toBe(false);
  });

  it('links each event to where it happened', () => {
    expect(linkOf(item({ run: '20261021-1030-ab12' }).event)).toBe('#teams/20261021-1030-ab12');
    expect(linkOf(item({ session: 'abcd1234' }).event)).toBe('#session/abcd1234');
    expect(linkOf(item({ source: 'daemon' }).event)).toBeNull();
  });
});
