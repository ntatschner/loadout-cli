// A list's query as the server sent it back, and the edits a toolbar makes to
// it. Nothing here parses a query: the daemon does, with the code the CLI and
// the TUI use, and says what it made of it in pieces. Removing a chip, adding
// a filter or choosing a sort is joining pieces again, so the chips and the
// text beside them are always the same string.

export interface ListField {
  name: string;
  type: 'text' | 'choice' | 'date' | 'number';
  description: string;
  choices: string[];
}

export interface ListShape {
  text: string;
  parts: string[];
  fields: ListField[];
  defaultQuery: string;
}

const SORT = 'sort:';

/** The query without one of its pieces: a chip taken away. */
export function withoutPart(parts: string[], index: number): string {
  return parts.filter((_, at) => at !== index).join(' ');
}

/** The query with a term put on the end, unless it is there already. */
export function withTerm(parts: string[], term: string): string {
  return (parts.includes(term) ? parts : [...parts, term]).join(' ');
}

/** The sort the query names, as written after 'sort:', or '' for the default order. */
export function sortOf(parts: string[]): string {
  return parts.find((part) => part.startsWith(SORT))?.slice(SORT.length) ?? '';
}

/** The query sorted by this instead, or in the default order for ''. */
export function withSort(parts: string[], sort: string): string {
  const kept = parts.filter((part) => !part.startsWith(SORT));

  return (sort ? [...kept, `${SORT}${sort}`] : kept).join(' ');
}

/** What a piece says, for a chip's label: a sort reads as one. */
export function partSaid(part: string): string {
  if (part.startsWith(SORT)) {
    const fields = part.slice(SORT.length).split(',');

    return `sorted by ${fields.map((field) => (field.startsWith('-') ? `${field.slice(1)}, descending` : field)).join(', then ')}`;
  }

  return part;
}
