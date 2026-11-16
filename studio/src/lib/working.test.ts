// Which views are working views, where notifications take a lane of their
// own: every one with a terminal or a split in it, and none of the others.
import { describe, expect, it } from 'vitest';
import { isWorkingView } from './reveal';
import type { StudioMode } from './modes';
import type { Route } from './router.svelte';

const at = (page: Route['page'], rest: string[] = [], beside?: string[]): Route => ({ page, rest, ...(beside ? { beside } : {}) });

describe('a working view', () => {
  it.each<[string, StudioMode, Route]>([
    ['the Terminals deck on Home', 'terminals', at('home')],
    ['the Terminals deck on Sessions', 'terminals', at('sessions')],
    ['a page open beside the deck', 'terminals', at('ideas')],
    ['a run open beside the deck', 'terminals', at('teams', ['20261116-0900-aaaa'])],
    ['the board', 'teams', at('home')],
    ['a page open beside the board', 'teams', at('settings')],
    ['a session open beside the board', 'teams', at('session', ['abc'])],
    ['a session\'s page in Default', 'default', at('session', ['abc'])],
    ['a session\'s page in Minimal', 'minimal', at('session', ['abc'])],
    ['the Pure Focus desk', 'pure', at('home')],
    ['a Pure Focus page desk', 'pure', at('memory')],
  ])('is %s', (_, mode, route) => {
    expect(isWorkingView(mode, route)).toBe(true);
  });

  it.each<[string, StudioMode, Route]>([
    ['Home in Default', 'default', at('home')],
    ['Ideas in Default', 'default', at('ideas')],
    ['the quiet Home in Minimal', 'minimal', at('home')],
    ['Commands in Terminals, which replaces the deck', 'terminals', at('commands')],
    ['the Teams list in Terminals', 'terminals', at('teams')],
    ['a run\'s page in Teams, outside the board', 'teams', at('teams', ['20261116-0900-aaaa'])],
    ['a Pure Focus place, which stays a page', 'pure', at('projects', ['+add'])],
  ])('is not %s', (_, mode, route) => {
    expect(isWorkingView(mode, route)).toBe(false);
  });
});
