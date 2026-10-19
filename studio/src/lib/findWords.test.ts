// The words half of the list query, for find boxes: the same words keep the
// same things here as they do in a toolbar's query, and nothing typed is
// refused, because the box filters as it is typed into.
import { describe, expect, it } from 'vitest';
import { findWords, matchesWords } from './findWords';
import { filtered, type PureGroup } from './pure';

describe('findWords', () => {
  it('reads words, quoted phrases whole and a minus as leaving out', () => {
    expect(findWords('Login  "release notes" -draft')).toEqual([
      { text: 'login', negated: false },
      { text: 'release notes', negated: false },
      { text: 'draft', negated: true },
    ]);
  });

  it('takes a colon as text and runs an unclosed quote to the end', () => {
    expect(findWords('state:open "half typed')).toEqual([
      { text: 'state:open', negated: false },
      { text: 'half typed', negated: false },
    ]);
  });

  it('looks for nothing in an empty phrase or a lone minus', () => {
    expect(findWords('"" - ')).toEqual([]);
  });
});

describe('matchesWords', () => {
  it('keeps an item only when every word is in one of its texts, ignoring case', () => {
    expect(matchesWords(['Fix the login page', 'web · claude'], 'login claude')).toBe(true);
    expect(matchesWords(['Fix the login page', 'web · claude'], 'login codex')).toBe(false);
  });

  it('matches a phrase within one text, not across two', () => {
    expect(matchesWords(['Fix the login', 'page'], '"login page"')).toBe(false);
    expect(matchesWords(['Fix the login page'], '"login page"')).toBe(true);
  });

  it('leaves out what has a negated word, and keeps everything for nothing typed', () => {
    expect(matchesWords(['Release notes draft'], 'release -draft')).toBe(false);
    expect(matchesWords(['Anything'], '   ')).toBe(true);
  });
});

describe('Pure Focus filter', () => {
  const groups = [
    {
      key: 'runs',
      label: 'Runs',
      items: [
        { id: 'a', title: 'Release crew', meta: 'web · waiting' },
        { id: 'b', title: 'Docs crew', meta: 'web · running' },
      ],
    },
  ] as unknown as PureGroup[];

  it('takes the words the way the list query does, rather than as one string', () => {
    expect(filtered(groups, 'crew waiting').flatMap((group) => group.items.map((item) => item.id))).toEqual(['a']);
    expect(filtered(groups, '-waiting').flatMap((group) => group.items.map((item) => item.id))).toEqual(['b']);
  });
});
