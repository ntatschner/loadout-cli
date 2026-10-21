/** Something the page reads again from the daemon. */
export type Reload = 'sessions' | 'history' | 'runs' | 'jobs' | 'tasks' | 'ideas' | 'eventlog';

/**
 * What the page reads again when the daemon says what has changed.
 *
 * The history goes with the sessions: a session starting or ending is what
 * changes which conversation is running, and what a restart left behind.
 *
 * Tasks are said when a list's file changed, from a terminal, an agent or a
 * pull as well as from here. Every list is read again rather than the one
 * named, because what moved away from a list is drawn on the list it left.
 * The ideas go with them: an idea is a row on a list, so capturing, removing
 * or accepting one changes the list as well as its record.
 *
 * Ideas are said when a record changed, or a round started or finished.
 *
 * The event history is said when an event is recorded or one is marked
 * read, here or by 'loadout events --mark-read'.
 */
export function reloadsFor(what: readonly string[]): Reload[] {
  const reloads: Reload[] = [];

  if (what.includes('sessions')) {
    reloads.push('sessions', 'history');
  }

  if (what.includes('runs')) {
    reloads.push('runs');
  }

  if (what.includes('jobs')) {
    reloads.push('jobs');
  }

  if (what.includes('tasks')) {
    reloads.push('tasks');
  }

  if (what.includes('tasks') || what.includes('ideas')) {
    reloads.push('ideas');
  }

  if (what.includes('eventlog')) {
    reloads.push('eventlog');
  }

  return reloads;
}
