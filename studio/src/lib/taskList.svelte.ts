// One task list as a page shows it: its query, what the daemon made of it,
// and the tasks it kept. The query goes to 'task list' as --filter, so the
// list is filtered by the same code a terminal uses. A change to the query is
// saved for the view, so the TUI and 'task list' start from it too.
import { readJson, Refused, SignedOut } from './api';
import { lineFor } from './commandLine';
import type { ListShape } from './lists';
import { studio } from './state.svelte';
import type { MovedAway, Task } from './tasks';

interface Listed {
  tasks: Task[];
  movedAway?: MovedAway[];
  recorded?: number;
  query?: ListShape;
}

/**
 * The words a query adds to 'task list'. Null is the command's own default,
 * hiding done and dropped work; an empty query is everything, which the
 * command spells --all. Joined with '=' because a query can start with a
 * minus, and the command line reads a separate one as an option.
 */
export function queryWords(query: string | null): string[] {
  return query === null ? [] : query === '' ? ['--all'] : [`--filter=${query}`];
}

export class TaskListView {
  tasks = $state<Task[] | null>(null);
  away = $state<MovedAway[]>([]);
  shape = $state<ListShape | null>(null);
  recorded = $state<number | null>(null);
  problem = $state<string | null>(null);
  /** What was asked for; null until somebody changes it, which is the default. */
  query = $state<string | null>(null);
  #where: string[] = [];
  #asked = 0;
  #opened = false;
  readonly #view: string | null;

  /** @param view The view whose query is saved, or null for one that keeps none. */
  constructor(view: string | null = 'tasks') {
    this.#view = view;
  }

  /** The query as the toolbar shows it: what was asked, or what the default came to. */
  get text(): string {
    return this.query ?? this.shape?.text ?? '';
  }

  /** The command the list is the same as. */
  get from(): string {
    return lineFor('task list', [...this.#where, ...queryWords(this.query)]);
  }

  /** Reads the list for this place again, with the query as it stands. */
  async load(where: string[] = this.#where): Promise<void> {
    this.#where = where;

    // The first read opens with what was saved for the view, if anything was.
    if (!this.#opened) {
      this.#opened = true;
      this.query ??= (this.#view && studio.prefs?.views?.[this.#view]) || null;
    }

    const mine = ++this.#asked;

    try {
      const read = await readJson<Listed>('task list', [...where, ...queryWords(this.query)]);

      if (mine !== this.#asked) {
        return;
      }

      this.tasks = read.tasks;
      this.away = read.movedAway ?? [];
      this.shape = read.query ?? null;
      this.recorded = read.recorded ?? null;
      this.problem = null;
    } catch (error) {
      if (mine !== this.#asked || error instanceof SignedOut) {
        return;
      }

      // A query the daemon refused keeps the list as it was, with why beside
      // the box; anything else is the list failing to read.
      if (error instanceof Refused && this.query) {
        this.problem = error.message;
      } else {
        this.tasks = null;
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

    if (this.#view && !this.problem && !studio.watchOnly) {
      const kept = query === this.shape?.defaultQuery ? '' : query;

      if (kept !== (studio.prefs?.views?.[this.#view] ?? '')) {
        await studio.setPreference(`studio-query-${this.#view}`, kept);
      }
    }
  }

  /** Forgets what was read, for another place to be read into. */
  clear(): void {
    this.tasks = null;
    this.away = [];
  }
}
