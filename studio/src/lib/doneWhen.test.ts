// Done-when on the Ideas page's Accept: what the box puts on the command line.
// Accepting with the plan's own conditions untouched confirms them and says
// nothing; a changed box replaces them; an emptied one says there are none.
import { describe, expect, it } from 'vitest';
import { acceptWords, doneWhenLines, doneWhenWords } from './ideas';

describe('doneWhenWords', () => {
  const proposed = ['every host is listed', 'a host that is down shows red'];

  it('adds nothing while the box holds what the plan proposed', () => {
    expect(doneWhenWords('every host is listed\n  a host that is down shows red  \n\n', proposed)).toEqual([]);
  });

  it('gives every condition when the box was changed, joined with = so a leading dash is not read as an option', () => {
    expect(doneWhenWords('every host is listed\n- down hosts show red', proposed)).toEqual([
      '--done-when=every host is listed',
      '--done-when=- down hosts show red',
    ]);
  });

  it('says there are none when the box was emptied', () => {
    expect(doneWhenWords('  \n', proposed)).toEqual(['--no-done-when']);
  });

  it('adds nothing when the plan proposed none and the box is still empty', () => {
    expect(doneWhenWords('', [])).toEqual([]);
  });
});

describe('doneWhenLines', () => {
  it('takes one condition a line, Windows line endings included, blank lines left out', () => {
    expect(doneWhenLines('one\r\n\r\n two \n')).toEqual(['one', 'two']);
  });
});

describe('acceptWords', () => {
  it('puts the done-when after where the plan goes', () => {
    expect(acceptWords('idea-1', ['--global'], { project: 'homelab' }, ['--done-when=it works'])).toEqual([
      'idea-1', '--global', '--to', 'homelab', '--done-when=it works',
    ]);
    expect(acceptWords('idea-1', [], { newProject: 'Lab' }, ['--no-done-when'])).toEqual(['idea-1', '--new-project=Lab', '--no-done-when']);
  });
});
