// The command each of Studio's actions runs, written as somebody would type
// it. Every action that changes something shows one of these beside it.
//
// They are the lines DashboardActions.Maps and DashboardTasks.Maps build for
// the same verbs, which is what the daemon runs when the page asks. Both
// sides are pinned to the same literal commands by tests (commands.test.ts
// and StudioCommandTests), so a change on one side that the other does not
// make fails a build rather than showing somebody the wrong command.
//
// The daemon also passes --non-interactive, which only says nobody is at a
// terminal to answer; the commands here are what a person would type.

/** One argument as a shell would need it: quoted when it holds a space, a quote, $, ` or a backslash, or nothing at all. */
export function quoted(value: string): string {
  // Also quoted and escaped: $, ` and \, which a shell would otherwise read,
  // so a line copied out of the page runs as shown. An empty value is "".
  return value === '' || /[\s"'$`\\]/.test(value) ? `"${value.replace(/(["\\$`])/g, '\\$1')}"` : value;
}

export const command = {
  /** Answering a run's question: POST /api/runs/{run}/gate. */
  gate: (run: string, gate: string | null, answer: string) =>
    `$ loadout team gate ${run}${gate ? ` --gate ${gate}` : ''} --answer ${quoted(answer)} --by dashboard`,

  /** Holding a run after its round: POST /api/runs/{run}/pause. */
  pause: (run: string) => `$ loadout team halt ${run} --pause`,

  /** Letting a held run carry on: POST /api/runs/{run}/resume. */
  resume: (run: string) => `$ loadout team halt ${run} --resume`,

  /** Stopping a run: POST /api/runs/{run}/stop. */
  stop: (run: string) => `$ loadout team halt ${run}`,

  /** A task's state: POST /api/tasks with verb "state". */
  task: (id: string, state: string, project: string | null) =>
    `$ loadout task declare ${id} ${state} ${project ? `--project ${project}` : '--global'}`,

  /** Another round of an idea: POST /api/ideas with verb "refine". */
  refine: (id: string) => `$ loadout idea refine ${id}`,

  /** Ending a hosted session: DELETE /api/v1/sessions/{id}. */
  // No loadout command ends a session, so say what it does instead.
  endSession: () => 'stops the agent in this terminal',

  /** Carrying a conversation on in a terminal. */
  resumeLast: () => '$ loadout resume --last',

  /** Signing every browser out: run in a terminal, or from Settings. */
  signOut: () => '$ loadout dashboard sign-out',

  /** Opening Studio signed in. */
  open: () => '$ loadout dashboard --open --studio',

  /** One of Studio's settings. */
  setting: (key: string, value: string) => `$ loadout config set ${key} ${value === '' ? '""' : quoted(value)}`,
};
