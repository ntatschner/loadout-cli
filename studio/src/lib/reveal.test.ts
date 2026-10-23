// Where every jump lands, mode by mode, and that nothing jumps any other way.
// The table is the whole policy: a session, a run, a team's page and a page,
// in each of the five modes, never changing the mode.
import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { besideNow, besideOf, besideTeams, placement, type Jump, type Target } from './reveal';
import { MODES, type StudioMode } from './modes';
import { hashOf, parse, type Route } from './router.svelte';

const home: Route = { page: 'home', rest: [] };
const projects: Route = { page: 'projects', rest: ['loadout-cli'] };

const session: Target = { kind: 'session', id: 's1' };
const run: Target = { kind: 'run', id: 'r1' };
const team: Target = { kind: 'team', name: 'reviewers' };
const page: Target = { kind: 'page', parts: ['events'], query: 'source=run' };

const at = (parts: string[], query = ''): Jump => ({ parts, query, focusing: null });

describe('where a jump lands', () => {
  const table: Record<StudioMode, [Jump, Jump, Jump, Jump]> = {
    default: [at(['session', 's1']), at(['teams', 'r1']), at(['teams', '+team', 'reviewers']), at(['events'], 'source=run')],
    terminals: [at(['session', 's1']), at(['teams', 'r1']), at(['teams', '+team', 'reviewers']), at(['events'], 'source=run')],
    teams: [at(['session', 's1']), at(['teams', 'r1']), at(['teams', '+team', 'reviewers']), at(['events'], 'source=run')],
    minimal: [
      { parts: ['home'], query: '', focusing: { kind: 'session', id: 's1' } },
      { parts: ['home'], query: '', focusing: { kind: 'team run', id: 'r1' } },
      at(['teams', '+team', 'reviewers']),
      at(['events'], 'source=run'),
    ],
    pure: [at(['session', 's1']), at(['teams', 'r1']), at(['teams', '+team', 'reviewers']), at(['events'], 'source=run')],
  };

  for (const mode of MODES.map((one) => one.key)) {
    it(`in ${mode}, for a session, a run, a team and a page`, () => {
      const [toSession, toRun, toTeam, toPage] = table[mode];

      expect(placement(mode, session, home)).toEqual(toSession);
      expect(placement(mode, run, home)).toEqual(toRun);
      expect(placement(mode, team, home)).toEqual(toTeam);
      expect(placement(mode, page, home)).toEqual(toPage);
    });
  }

  it('covers every mode there is', () => {
    expect(Object.keys(table).sort()).toEqual(MODES.map((one) => one.key).sort());
  });

  it('keeps the page open beside the terminals when a session is shown in Terminals', () => {
    expect(placement('terminals', session, projects)).toEqual(at(['session', 's1'], 'beside=projects/loadout-cli'));
    expect(placement('terminals', { ...session, launched: true }, projects)).toEqual(at(['session', 's1'], 'beside=projects/loadout-cli'));
    // A run or a team's page beside the deck stays too, and so does a page an address already keeps beside.
    expect(placement('terminals', session, { page: 'teams', rest: ['r1'] })).toEqual(at(['session', 's1'], 'beside=teams/r1'));
    expect(placement('terminals', session, { page: 'session', rest: ['s0'], beside: ['ideas'] })).toEqual(at(['session', 's1'], 'beside=ideas'));
  });

  it('keeps nothing beside in any other mode, where the board or the page has the one place beside', () => {
    for (const mode of ['default', 'teams', 'pure', 'minimal'] as StudioMode[]) {
      expect(placement(mode, session, projects).query).toBe('');
    }
  });
});

describe('what Terminals keeps beside the deck', () => {
  it('is one of the six, a run, or a team, and none of the Teams places', () => {
    expect(besideNow(projects)).toEqual(['projects', 'loadout-cli']);
    expect(besideNow({ page: 'teams', rest: ['r1'] })).toEqual(['teams', 'r1']);
    expect(besideNow({ page: 'teams', rest: ['+team', 'reviewers'] })).toEqual(['teams', '+team', 'reviewers']);
    expect(besideNow({ page: 'teams', rest: ['+remedies'] })).toBeNull();
    expect(besideNow({ page: 'teams', rest: [] })).toBeNull();
    expect(besideNow({ page: 'events', rest: [] })).toBeNull();
    expect(besideNow(home)).toBeNull();
  });

  it('reads a run and a team as beside, and the Teams list and its places as pages', () => {
    expect(besideTeams(['r1'])).toBe(true);
    expect(besideTeams(['+team', 'x'])).toBe(true);
    expect(besideTeams(['+team'])).toBe(false);
    expect(besideTeams(['+bin'])).toBe(false);
    expect(besideTeams([])).toBe(false);
  });
});

describe('what each mode draws beside its deck or board', () => {
  const session1: Route = { page: 'session', rest: ['s1'] };
  const withBeside: Route = { page: 'session', rest: ['s1'], beside: ['projects', 'loadout-cli'] };
  const aRun: Route = { page: 'teams', rest: ['r1'] };

  it('in Terminals: the page a session keeps open, one of the six, a run or a team; never a session itself', () => {
    expect(besideOf('terminals', withBeside)).toEqual({ page: 'projects', rest: ['loadout-cli'] });
    expect(besideOf('terminals', projects)).toEqual({ page: 'projects', rest: ['loadout-cli'] });
    expect(besideOf('terminals', aRun)).toEqual({ page: 'teams', rest: ['r1'] });
    expect(besideOf('terminals', session1)).toBeNull();
    expect(besideOf('terminals', { page: 'teams', rest: ['+bin'] })).toBeNull();
  });

  it('in Teams: a session beside the board, as a run is, and the six', () => {
    expect(besideOf('teams', session1)).toEqual({ page: 'session', rest: ['s1'] });
    expect(besideOf('teams', projects)).toEqual({ page: 'projects', rest: ['loadout-cli'] });
    expect(besideOf('teams', home)).toBeNull();
  });

  it('nothing beside in the modes without a deck or a board', () => {
    for (const mode of ['default', 'minimal', 'pure'] as StudioMode[]) {
      expect(besideOf(mode, withBeside)).toBeNull();
      expect(besideOf(mode, session1)).toBeNull();
      expect(besideOf(mode, projects)).toBeNull();
    }
  });
});

describe('the address with a page beside', () => {
  it('reads the page beside off a session, Home and Sessions, and keeps it through a round trip', () => {
    expect(parse('#session/s1?beside=projects/loadout-cli')).toEqual({ page: 'session', rest: ['s1'], query: '', beside: ['projects', 'loadout-cli'] });
    expect(parse('#home?beside=teams/r1')).toEqual({ page: 'home', rest: [], query: '', beside: ['teams', 'r1'] });
    expect(parse(hashOf(['session', 'a b'], 'beside=memory/x%2Fy'))).toEqual({ page: 'session', rest: ['a b'], query: '', beside: ['memory', 'x/y'] });
  });

  it('leaves every other query as it was written, and an old address without one as it was', () => {
    expect(parse('#events?beside=projects')).toEqual({ page: 'events', rest: [], query: 'beside=projects' });
    expect(parse('#session/s1')).toEqual({ page: 'session', rest: ['s1'], query: '' });
    expect(parse('#projects')).toEqual({ page: 'projects', rest: [], query: '' });
  });
});

describe('the one way to jump', () => {
  // Every jump goes through reveal(), so the mode decides where it lands. The
  // old ways sent the address straight to a session, which threw away the
  // page beside the terminals and in Teams left the board. The router is the
  // one place that sets the address, and reveal() the one that decides it.
  const sources = readdirSync('src', { recursive: true, encoding: 'utf-8' })
    .map((name) => name.replaceAll('\\', '/'))
    .filter((name) => /\.(ts|svelte)$/.test(name) && !/\.test\.ts$/.test(name) && !['lib/reveal.svelte.ts', 'lib/reveal.ts', 'lib/router.svelte.ts'].includes(name));

  const BANNED: [RegExp, string][] = [
    [/router\.go(With)?\(\s*\[?\s*['"]session['"]/, "router.go('session', …)"],
    [/location\.hash\s*=[^=]/, 'location.hash = …'],
    [/router\.go\.bind\(/, 'router.go.bind(router) handed out as a way to jump'],
  ];

  it('reads the sources it checks', () => {
    expect(sources).toContain('App.svelte');
    expect(sources).toContain('lib/state.svelte.ts');
    expect(sources.length).toBeGreaterThan(50);
  });

  it('is reveal(): nothing else sends the address to a session or sets it by hand', () => {
    const found = sources.flatMap((name) => {
      const text = readFileSync(`src/${name}`, 'utf-8');

      return BANNED.filter(([pattern]) => pattern.test(text)).map(([, what]) => `${name}: ${what}`);
    });

    expect(found).toEqual([]);
  });

  it('catches each of the old ways', () => {
    const caught = (text: string) => BANNED.some(([pattern]) => pattern.test(text));

    expect(caught("router.go('session', id);")).toBe(true);
    expect(caught('router.go("session", started.id)')).toBe(true);
    expect(caught('void (location.hash = `#session/${id}`)')).toBe(true);
    expect(caught('paletteGroups(studio, router.go.bind(router))')).toBe(true);
    expect(caught("router.go('teams', id); if (location.hash === from) {}")).toBe(false);
  });
});
