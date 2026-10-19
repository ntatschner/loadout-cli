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
  /** Where it was moved from, when it was: a project's slug, or null for the workspace-wide list. */
  moved?: { from: string | null; at: string } | null;
}

/** The task a launch sheet opens with, and the project it is on. */
export interface Chosen {
  project: string;
  id: string;
  title: string;
  state: string;
}

/** A task that left a list, as that list shows it. It is not one of the list's tasks. */
export interface MovedAway {
  id: string;
  title: string;
  kind?: string;
  /** The project it is on now, or null for the workspace-wide list. */
  to: string | null;
  at: string;
}

/** A list as a person names it. */
export function whereSaid(project: string | null | undefined): string {
  return project || 'the workspace-wide list';
}

/** Where Studio shows a list's tasks. */
export function listHref(project: string | null | undefined): string {
  return project ? `#projects/${encodeURIComponent(project)}` : '#projects/+tasks';
}

/** Where a moved task came from, in words. */
export function movedSaid(moved: { from: string | null; at: string }): string {
  return `from ${whereSaid(moved.from)}, ${new Date(moved.at).toLocaleDateString([], { day: 'numeric', month: 'short' })}`;
}

export const TASK_NEXT: Record<string, TaskState[]> = {
  open: ['doing', 'blocked', 'dropped'],
  doing: ['done', 'blocked', 'open'],
  blocked: ['doing', 'dropped'],
  done: ['open'],
  dropped: ['open'],
};

export const TASK_MOVE: Record<TaskState, string> = {
  doing: 'Mark it doing',
  done: 'Mark it done',
  blocked: 'It is blocked',
  dropped: 'Drop it',
  open: 'Reopen it',
};

/**
 * Whether a task can have a session started on it. A dropped task is
 * abandoned work, and the launcher refuses to link a session to one.
 */
export function startable(state: string): boolean {
  return state !== 'dropped';
}

/**
 * Where starting a session on a task moves it, or null to leave it where it
 * is: open and blocked work is under way once a session is on it, and work
 * already doing or done keeps its state, the session being another one on it.
 */
export function startedState(state: string): TaskState | null {
  return state === 'open' || state === 'blocked' ? 'doing' : null;
}

/** The words for starting a session on a task, after 'launch'. */
export function startWords(project: string, id: string): string[] {
  return [project, '--task-id', id];
}

/** The words for moving a task, after 'task declare'. */
export function declareWords(id: string, state: string, where: string[]): string[] {
  return [id, state, ...where];
}
