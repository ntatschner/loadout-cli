// Where a task can go from where it stands, and what each move is called:
// every move is 'loadout task declare <id> <state>'.

export type TaskState = 'open' | 'doing' | 'blocked' | 'done' | 'dropped';

export interface Task {
  id: string;
  title: string;
  state: string;
  kind?: string;
  declaredBy?: string;
  declared?: string;
  note?: string | null;
}

export const TASK_NEXT: Record<string, TaskState[]> = {
  open: ['doing', 'blocked', 'dropped'],
  doing: ['done', 'blocked', 'open'],
  blocked: ['doing', 'dropped'],
  done: ['open'],
  dropped: ['open'],
};

export const TASK_MOVE: Record<TaskState, string> = {
  doing: 'Start it',
  done: 'Mark it done',
  blocked: 'It is blocked',
  dropped: 'Drop it',
  open: 'Reopen it',
};

/** The words for moving a task, after 'task declare'. */
export function declareWords(id: string, state: string, where: string[]): string[] {
  return [id, state, ...where];
}
