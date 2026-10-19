// One task list as a page shows it: the shared list, with what moved away
// from it kept apart from its tasks, so no count includes one.
import { CommandList, listWords } from './commandList.svelte';
import type { MovedAway, Task } from './tasks';

/**
 * The words a query adds to 'task list'. Null is the command's own default,
 * hiding done and dropped work; an empty query is everything, which the
 * command spells --all.
 */
export function queryWords(query: string | null): string[] {
  return listWords(query, ['--all']);
}

export class TaskListView extends CommandList<Task> {
  away = $state<MovedAway[]>([]);

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
  }

  override clear(): void {
    super.clear();
    this.away = [];
  }
}
