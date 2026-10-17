import { describe, expect, it } from 'vitest';
import type { StudioCommand, StudioOption } from './api';
import { lineFor, problemWith, quoted, readsWith, wordsFor } from './commandLine';

function option(name: string, kind: StudioOption['kind'], more: Partial<StudioOption> = {}): StudioOption {
  return { name, short: null, kind, valueName: null, description: '', required: false, global: false, choices: [], ...more };
}

const show: StudioCommand = {
  path: 'memory show',
  description: 'Show a topic',
  category: 'Memory',
  example: '',
  runnable: true,
  reach: { page: 'commands', effect: 'reads', reason: null, changesOnlyWith: null },
  arguments: [{ name: 'TOPIC', position: 0, required: true, many: false, description: '' }],
  options: [option('--project', 'value'), option('--json', 'flag', { global: true }), option('--scope', 'value', { choices: ['project', 'user'] })],
};

describe('the words a form stands for', () => {
  it('puts the arguments first, then each option given, in the order the command declares them', () => {
    expect(wordsFor(show, { TOPIC: 'build-quirks', '--project': 'starstats', '--json': true })).toEqual([
      'build-quirks',
      '--project',
      'starstats',
      '--json',
    ]);
  });

  it('leaves out a flag that is off and a value that is empty', () => {
    expect(wordsFor(show, { TOPIC: 'build-quirks', '--project': '  ', '--json': false })).toEqual(['build-quirks']);
  });

  it('joins a value that starts with a dash to its option, since the parser refuses it as the next word', () => {
    expect(wordsFor(show, { TOPIC: 't', '--project': '-odd' })).toEqual(['t', '--project=-odd']);
  });

  it('repeats an option once for each line of a many-valued one', () => {
    const add: StudioCommand = { ...show, path: 'memory write', arguments: [], options: [option('--fact', 'values')] };

    expect(wordsFor(add, { '--fact': 'one\n\n two \nthree' })).toEqual(['--fact', 'one', '--fact', 'two', '--fact', 'three']);
  });

  it('gives an optional-valued option bare when it is ticked, and with its value when one is typed', () => {
    const accessible: StudioCommand = { ...show, arguments: [], options: [option('--accessible', 'optional')] };

    expect(wordsFor(accessible, { '--accessible': true })).toEqual(['--accessible']);
    expect(wordsFor(accessible, { '--accessible': 'screen-reader' })).toEqual(['--accessible=screen-reader']);
  });
});

describe('what stops a form being run', () => {
  it('names a required argument that is missing', () => {
    expect(problemWith(show, {})).toBe('memory show needs its topic.');
  });

  it('refuses an argument that starts with a dash, which the command would read as an option', () => {
    expect(problemWith(show, { TOPIC: '--json' })).toMatch(/cannot start with a dash/);
  });

  it('names a required option that is missing', () => {
    const needs: StudioCommand = { ...show, arguments: [], options: [option('--team', 'value', { required: true })] };

    expect(problemWith(needs, {})).toBe('memory show needs --team.');
    expect(problemWith(needs, { '--team': 'release-crew' })).toBeNull();
  });
});

describe('the line shown', () => {
  it('quotes only the words a shell would need quoted, as the daemon does', () => {
    expect(quoted('build-quirks')).toBe('build-quirks');
    expect(quoted('two words')).toBe("'two words'");
    expect(quoted("it's")).toBe(`'it'\\''s'`);
  });

  it('is the command and its words after the prompt', () => {
    expect(lineFor('memory show', ['build-quirks', '--project', 'my project'])).toBe("$ loadout memory show build-quirks --project 'my project'");
  });
});

describe('whether a form only reads', () => {
  const drift: StudioCommand = { ...show, path: 'drift', reach: { page: 'commands', effect: 'changes', reason: null, changesOnlyWith: ['--fix'] } };
  const remove: StudioCommand = { ...show, path: 'memory delete', reach: { page: 'commands', effect: 'changes', reason: null, changesOnlyWith: null } };

  it('reads when the command acts only with an option it is not given, as the daemon decides', () => {
    expect(readsWith(drift, [])).toBe(true);
    expect(readsWith(drift, ['--fix'])).toBe(false);
  });

  it('reads for a preview of anything that changes something, and not otherwise', () => {
    expect(readsWith(remove, ['old'])).toBe(false);
    expect(readsWith(remove, ['old', '--dry-run'])).toBe(true);
  });
});
