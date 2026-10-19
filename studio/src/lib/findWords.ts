// The words half of the list query, for the find boxes that hold no one kind
// of thing: Pure Focus, which lists every kind at once, and a run's Outbox,
// which lists files. They have no fields to filter on, so they take only what
// the query does with words, matched as ListFields matches them: every word in
// one of the item's texts, ignoring case, a quoted phrase whole, and a word
// with a minus in front leaving out what has it.
//
// Unlike the query a toolbar sends, nothing here is refused. A box filters as
// it is typed into, so a colon is only text and a quote not yet closed runs to
// the end of what was typed.

export interface FindWord {
  text: string;
  negated: boolean;
}

/** What was typed, as words. */
export function findWords(typed: string): FindWord[] {
  const words: FindWord[] = [];
  let current = '';
  let inQuotes = false;

  const flush = () => {
    const negated = current.length > 1 && current.startsWith('-');
    const text = (negated ? current.slice(1) : current).toLowerCase();

    if (text.length > 0 && text !== '-') {
      words.push({ text, negated });
    }

    current = '';
  };

  for (const c of typed) {
    if (c === '"') {
      inQuotes = !inQuotes;
    } else if (/\s/.test(c) && !inQuotes) {
      flush();
    } else {
      current += c;
    }
  }

  flush();

  return words;
}

/** Whether an item with these texts is kept by what was typed. */
export function matchesWords(texts: (string | null | undefined)[], typed: string): boolean {
  const lowered = texts.filter((text): text is string => !!text).map((text) => text.toLowerCase());

  return findWords(typed).every((word) => lowered.some((text) => text.includes(word.text)) !== word.negated);
}
