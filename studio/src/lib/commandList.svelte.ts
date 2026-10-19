// One list as a page shows it, read through its command with the shared
// query: what was asked, what the daemon made of it, and what it kept. The
// query goes to the command as --filter, so the list is filtered by the same
// code a terminal uses, and a change to it is saved for the view, so the TUI
// and the command start from it too.
import { readJson, Refused, SignedOut } from './api';
import { lineFor } from './commandLine';
import type { ListShape } from './lists';
import { studio } from './state.svelte';

/**
 * The words a query adds to a list command. Null is the command's own
 * default; an empty query is everything, spelled as the command spells it.
 * Joined with '=' because a query can start with a minus, and the command
 * line reads a separate one as an option.
 */
export function listWords(query: string | null, everything: string[] = []): string[] {
  return query === null ? [] : query === '' ? everything : [`--filter=${query}`];
}

export class CommandList<T> {
  items = $state<T[] | null>(null);
  shape = $state<ListShape | null>(null);
  recorded = $state<number | null>(null);
  problem = $state<string | null>(null);
  /** The last read whole, for what a list carries beside its items. */
  read = $state.raw<Record<string, unknown> | null>(null);
  /** Why the list could not be read at all, or null. */
  error = $state<string | null>(null);
  /** What was asked for; null until somebody changes it, which is the default. */
  query = $state<string | null>(null);
  #where: string[] = [];
  #asked = 0;
  #opened = false;

  /**
   * @param command The list command, as 'project list'.
   * @param key Where its --json keeps the items.
   * @param view The view whose query is saved, or null for one that keeps none.
   * @param everything How the command is asked for everything, when its default hides some.
   */
  constructor(
    readonly command: string,
    readonly key: string,
    readonly view: string | null,
    readonly everything: string[] = [],
  ) {}

  /** The query as the toolbar shows it: what was asked, or what the default came to. */
  get text(): string {
    return this.query ?? this.shape?.text ?? '';
  }

  /** The command the list is the same as. */
  get from(): string {
    return lineFor(this.command, [...this.#where, ...listWords(this.query, this.everything)]);
  }

  /** What else a read carried, for a list that keeps more than its items. */
  protected took(_read: Record<string, unknown>): void {}

  /** Reads the list: through its command, unless a list read another way says otherwise. */
  protected fetch(where: string[], query: string | null): Promise<Record<string, unknown>> {
    return readJson<Record<string, unknown>>(this.command, [...where, ...listWords(query, this.everything)]);
  }

  /** Reads the list for this place again, with the query as it stands. */
  async load(where: string[] = this.#where): Promise<void> {
    this.#where = where;

    // The first read opens with what was saved for the view, if anything was.
    if (!this.#opened) {
      this.#opened = true;
      this.query ??= (this.view && studio.prefs?.views?.[this.view]) || null;
    }

    const mine = ++this.#asked;

    try {
      const read = await this.fetch(where, this.query);

      if (mine !== this.#asked) {
        return;
      }

      this.read = read;
      this.items = (read[this.key] as T[] | undefined) ?? [];
      this.shape = (read.query as ListShape | undefined) ?? null;
      this.recorded = (read.recorded as number | undefined) ?? null;
      this.problem = null;
      this.error = null;
      this.took(read);
    } catch (error) {
      if (mine !== this.#asked) {
        return;
      }

      if (error instanceof SignedOut) {
        studio.signedOut = true;

        return;
      }

      // A query the daemon refused keeps the list as it was, with why beside
      // the box; anything else is the list failing to read.
      if (error instanceof Refused && this.query) {
        this.problem = error.message;
      } else {
        this.items = null;
        this.error = (error as Error).message;
      }
    }
  }

  /**
   * Asks for the list again with this query, and keeps it for the view. The
   * default and an empty query are kept as nothing, which the view opens
   * with as its default.
   */
  async ask(query: string): Promise<void> {
    this.query = query;
    await this.load();

    if (this.view && !this.problem && !studio.watchOnly) {
      const kept = query === this.shape?.defaultQuery ? '' : query;

      if (kept !== (studio.prefs?.views?.[this.view] ?? '')) {
        await studio.setPreference(`studio-query-${this.view}`, kept);
      }
    }
  }

  /** Forgets what was read, for another place to be read into. */
  clear(): void {
    this.items = null;
  }
}
