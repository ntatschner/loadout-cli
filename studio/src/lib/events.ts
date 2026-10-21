// The Events page's own logic, kept out of the component so it can be
// argued with in tests: the filter as the address holds it, the days the
// list is grouped into, and where each event goes.
import type { FeedItem, UserEvent } from './api';

export const LEVELS = ['needs', 'problem', 'done', 'info'] as const;
export const SOURCES = ['run', 'session', 'daemon', 'notice'] as const;
export const DIRECTIONS = ['to-me', 'by-me', 'any'] as const;

export type Level = (typeof LEVELS)[number];
export type Source = (typeof SOURCES)[number];
export type Direction = (typeof DIRECTIONS)[number];

/** The filter, as the page's controls hold it. */
export interface Filter {
  levels: Level[];
  project: string;
  sources: Source[];
  direction: Direction;
  since: string;
  until: string;
  unread: boolean;
  text: string;
}

export const LEVEL_WORDS: Record<Level, string> = { needs: 'Needs you', problem: 'Problems', done: 'Done', info: 'For your information' };
export const SOURCE_WORDS: Record<Source, string> = { run: 'Team runs', session: 'Sessions', daemon: 'The daemon', notice: 'Notices sent' };
export const DIRECTION_WORDS: Record<Direction, string> = { 'to-me': 'Said to you', 'by-me': 'Done by you', any: 'Both' };

/** The spans the page offers; anything else typed into the address is kept as it is. */
export const SINCE: [string, string][] = [
  ['', 'All kept'],
  ['1h', 'The last hour'],
  ['1d', 'The last day'],
  ['7d', 'The last week'],
];

/**
 * Reads the address's query into the controls. The daemon reads the same
 * string and is the judge of it; this only has to turn it into choices,
 * and drops what it does not know rather than refusing.
 */
export function parseFilter(query: string): Filter {
  const read = new URLSearchParams(query.replace(/^\?/, ''));
  const words = (key: string) => (read.get(key) ?? '').split(',').map((one) => one.trim().toLowerCase()).filter(Boolean);
  const direction = (read.get('direction') ?? 'to-me').toLowerCase();

  return {
    levels: LEVELS.filter((one) => words('level').includes(one)),
    project: read.get('project') ?? '',
    sources: SOURCES.filter((one) => words('source').includes(one)),
    direction: (DIRECTIONS as readonly string[]).includes(direction) ? (direction as Direction) : 'to-me',
    since: read.get('since') ?? '',
    until: read.get('until') ?? '',
    unread: ['true', '1', ''].includes(read.get('unread') ?? 'false'),
    text: read.get('text') ?? '',
  };
}

/**
 * The canonical string, in the order 'loadout events --filter' writes it:
 * level, project, source, direction, since, until, unread, text, with the
 * defaults left out.
 */
export function formatFilter(filter: Filter): string {
  const parts: string[] = [];
  const add = (key: string, value: string) => {
    if (value) {
      parts.push(`${key}=${encodeURIComponent(value)}`);
    }
  };

  add('level', LEVELS.filter((one) => filter.levels.includes(one)).join(','));
  add('project', filter.project.trim());
  add('source', SOURCES.filter((one) => filter.sources.includes(one)).join(','));
  add('direction', filter.direction === 'to-me' ? '' : filter.direction);
  add('since', filter.since.trim());
  add('until', filter.until.trim());
  add('unread', filter.unread ? 'true' : '');
  add('text', filter.text.trim());

  return parts.join('&');
}

/** The list cut into days, newest first, each headed as somebody would say it. */
export function byDay(items: FeedItem[], now: Date): { day: string; items: FeedItem[] }[] {
  const days: { day: string; items: FeedItem[] }[] = [];
  const today = new Date(now);
  today.setHours(0, 0, 0, 0);

  for (const item of items) {
    const at = new Date(item.event.at);
    const start = new Date(at);
    start.setHours(0, 0, 0, 0);

    const back = Math.round((today.getTime() - start.getTime()) / 86_400_000);
    const day =
      back === 0 ? 'Today' : back === 1 ? 'Yesterday' : at.toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'long' });

    if (days.at(-1)?.day === day) {
      days.at(-1)!.items.push(item);
    } else {
      days.push({ day, items: [item] });
    }
  }

  return days;
}

/** How long a reason stayed true, as the row says it: "resolved 4m later". */
export function resolvedAfter(item: FeedItem): string | null {
  if (!item.cleared) {
    return null;
  }

  const minutes = Math.max(0, Math.round((new Date(item.cleared).getTime() - new Date(item.event.at).getTime()) / 60_000));

  if (minutes < 1) {
    return 'resolved within a minute';
  }

  if (minutes < 60) {
    return `resolved ${minutes}m later`;
  }

  const hours = Math.floor(minutes / 60);

  return hours < 24 ? `resolved ${hours}h ${minutes % 60}m later` : `resolved ${Math.floor(hours / 24)}d later`;
}

/** Where an event happened, as a Studio address, or null for one that happened nowhere in particular. */
export function linkOf(event: UserEvent): string | null {
  if (event.run) {
    return `#teams/${encodeURIComponent(event.run)}`;
  }

  if (event.session) {
    return `#session/${encodeURIComponent(event.session)}`;
  }

  return null;
}

/** The kinds that are reasons, which clear themselves; everything else is a moment, which never does. */
export const REASONS = ['asking', 'going-nowhere', 'quiet', 'spending', 'waiting', 'approval'];

/** Whether this is a reason still true: one that has not cleared. */
export function outstanding(item: FeedItem): boolean {
  return REASONS.includes(item.event.kind) && !item.cleared;
}
