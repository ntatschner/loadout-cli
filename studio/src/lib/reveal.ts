// Where a jump lands. Everything in Studio that takes you somewhere, the
// palette, a toast, the waiting count, an Open button or a launch, names
// what it wants shown, and this table says where the current mode shows it.
// No jump ever changes the mode: where a mode cannot show a thing exactly,
// it shows the closest form it has.
//
//               a session              a run                 a team's page          a page
//   Default     its page               its page              its page               the page
//   Terminals   a pane of the deck,    beside the deck       beside the deck        the six beside the deck,
//               keeping a page beside                                               the rest as pages
//   Teams       beside the board       beside the board      beside the board       the six beside the board,
//                                                                                   the rest as pages
//   Minimal     its frame over the     its frame over the    its page               the quiet column
//               column                 column
//   Pure        the desk               the desk              its page, flat         a desk, or the page flat
//
// Kept apart from the state so the whole policy can be read, and tested, as
// data.

import { isSixPage, type StudioMode } from './modes';
import { besideQuery, type Route } from './router.svelte';

/** Something a jump wants shown. */
export type Target =
  | { kind: 'session'; id: string; /** Just started, so the deck finds it a pane of its own. */ launched?: boolean }
  | { kind: 'run'; id: string }
  | { kind: 'team'; name: string }
  | { kind: 'page'; parts: string[]; query?: string };

/** Where a jump lands: an address, and in Minimal what its column opens in the frame. */
export interface Jump {
  parts: string[];
  query: string;
  focusing: { kind: 'session' | 'team run'; id: string } | null;
}

/** Whether a Teams address is one Terminals shows beside the deck: a run, or a team's page. */
export function besideTeams(rest: string[]): boolean {
  const [first, second] = rest;

  return !!first && (!first.startsWith('+') || (first === '+team' && !!second));
}

/** What is open beside the terminals now, which a session shown in Terminals keeps open. */
export function besideNow(route: Route): string[] | null {
  if (route.beside?.length) {
    return route.beside;
  }

  if (isSixPage(route.page)) {
    return [route.page, ...route.rest];
  }

  if (route.page === 'teams' && besideTeams(route.rest)) {
    return ['teams', ...route.rest];
  }

  return null;
}

/**
 * What a mode shows beside its deck or its board for an address: one of the
 * six, a run or a team's page beside the terminals, which a session's address
 * can name as the page it keeps open; one of the six or a session beside the
 * runs board. Null where the address is not one shown beside anything.
 */
export function besideOf(mode: StudioMode, route: Route): { page: string; rest: string[] } | null {
  if (mode === 'terminals') {
    const [first = '', ...rest] = route.beside ?? [route.page, ...route.rest];

    return isSixPage(first) || (first === 'teams' && besideTeams(rest)) ? { page: first, rest } : null;
  }

  if (mode === 'teams' && (isSixPage(route.page) || route.page === 'session')) {
    return { page: route.page, rest: route.rest };
  }

  return null;
}

const to = (parts: string[], query = ''): Jump => ({ parts, query, focusing: null });

export function placement(mode: StudioMode, target: Target, route: Route): Jump {
  switch (target.kind) {
    case 'page':
      return to(target.parts, target.query ?? '');
    case 'team':
      return to(['teams', '+team', target.name]);
    case 'run':
      return mode === 'minimal' ? { ...to(['home']), focusing: { kind: 'team run', id: target.id } } : to(['teams', target.id]);
    case 'session': {
      if (mode === 'minimal') {
        return { ...to(['home']), focusing: { kind: 'session', id: target.id } };
      }

      if (mode === 'terminals') {
        return to(['session', target.id], besideQuery(besideNow(route) ?? undefined));
      }

      return to(['session', target.id]);
    }
  }
}
