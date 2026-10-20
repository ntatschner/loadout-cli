// One task list as a page shows it: the shared list, with what moved away
// from it kept apart from its tasks, so no count includes one. A list read a
// page at a time says how many there are in all and how many pages.
import { tasks as taskRoutes, listOf, Refused } from './api';
import { CommandList, listWords } from './commandList.svelte';
import { fromRoute, withTask, type MovedAway, type Task } from './tasks';

/**
 * The words a query adds to 'task list'. Null is the command's own default,
 * hiding done and dropped work; an empty query is everything, which the
 * command spells --all.
 */
export function queryWords(query: string | null): string[] {
  return listWords(query, ['--all']);
}

/** The words that read one page of a list, after the words naming it. */
export function pageWords(page: number, limit: number): string[] {
  return ['--limit', String(limit), '--page', String(page)];
}

export class TaskListView extends CommandList<Task> {
  away = $state<MovedAway[]>([]);
  /** How many the query kept on every page, or null before the first read. */
  total = $state<number | null>(null);
  /** How many pages there are; 1 for a list not read in pages. */
  pages = $state(1);
  /** The revision of the list the shown tasks were read from, which a change sends so a stale one is refused. */
  revision = $state<string | null>(null);

  /** @param view The view whose query is saved, or null for one that keeps none. */
  constructor(view: string | null = 'tasks') {
    super('task list', 'tasks', view, ['--all']);
  }

  /** The tasks the query kept, or null before the first read or after a failed one. */
  get tasks(): Task[] | null {
    return this.items;
  }

  protected override took(read: Record<string, unknown>): void {
    this.away = (read.movedAway as MovedAway[] | undefined) ?? [];
    this.total = (read.total as number | undefined) ?? this.items?.length ?? null;
    this.pages = (read.pages as number | undefined) ?? 1;
    this.revision = (read.revision as string | undefined) ?? null;
  }

  /** Shows a task as a change answered with it, and the revision the change left the list at. */
  applied(task: Task, revision: string | null): void {
    if (this.items) {
      this.items = withTask(this.items, task);
    }

    this.revision = revision;
  }

  /**
   * Changes a task's state through the task route, shows it as the daemon
   * answered with it, then reads the list again so its query still holds.
   * Sent with the revision the list was read at: 'stale' when somebody else
   * changed the list since, which changes nothing and reads it again. Any
   * other refusal is thrown, with the daemon's reason.
   */
  async change(task: Task, state: string, where: string[]): Promise<'changed' | 'stale'> {
    let outcome: 'changed' | 'stale' = 'changed';

    try {
      const done = await taskRoutes.change(listOf(where), task.id, { state }, this.revision);

      this.applied(fromRoute(done.task), done.revision);
    } catch (error) {
      if (!(error instanceof Refused && error.status === 409)) {
        throw error;
      }

      outcome = 'stale';
    }

    await this.load(where);

    return outcome;
  }

  override clear(): void {
    super.clear();
    this.away = [];
    this.total = null;
    this.pages = 1;
    this.revision = null;
  }
}
