// What can be done to a line, as HaloButtons that each show the command they
// run. Watching only, every one is shown with its command and does nothing.
import { command } from './commands';
import type { Work } from './lines';
import type { Studio } from './state.svelte';
import type { HaloButtonOptions } from './types';

export type Action = HaloButtonOptions & { onpress?: () => unknown };

export function actionsFor(studio: Studio, line: Work, go: (...parts: string[]) => void): Action[] {
  const actions = all(studio, line, go);

  if (!studio.watchOnly) {
    return actions;
  }

  // Opening something changes nothing, so it stays; everything else shows its
  // command for a terminal instead.
  return actions.map((action) => (action.changes === false ? action : { ...action, onpress: undefined, cmd: undefined, disabledReason: `${action.cmd} · in a terminal` }));
}

type Changing = Action & { changes?: boolean };

function all(studio: Studio, line: Work, go: (...parts: string[]) => void): Changing[] {
  if (line.kind === 'team run' && line.waiting && line.gate) {
    const gate = line.gate;
    const options = gate.options.length ? gate.options : ['yes', 'no'];

    return options.map((answer, index) => ({
      label: answer,
      kind: index === 0 ? 'needs' : '',
      solid: answer === gate.recommendation,
      glyph: index === 0 ? '✓' : undefined,
      cmd: command.gate(line.ref, gate.id, answer),
      onpress: () => studio.answerGate(line, answer),
    }));
  }

  if (line.kind === 'team run' && line.run?.running) {
    return [
      { label: 'Hold after this round', cmd: command.pause(line.ref), onpress: () => studio.hold(line) },
      { label: 'Let it carry on', cmd: command.resume(line.ref), onpress: () => studio.carryOn(line) },
    ];
  }

  if (line.kind === 'idea' && (line.failed || line.idea?.detail.stage === 'captured')) {
    return [{ label: line.failed ? 'Try the round again' : 'Refine it', cmd: command.refine(line.ref), onpress: () => studio.refine(line) }];
  }

  if (line.kind === 'idea') {
    return [{ label: 'Open the idea', cmd: 'opens Ideas · changes nothing', changes: false, onpress: () => go('ideas') }];
  }

  if (line.kind === 'task') {
    if (line.blocked || line.stage === 0) {
      return [{ label: 'Mark it doing', cmd: command.task(line.ref, 'doing', line.project), onpress: () => studio.taskState(line, 'doing') }];
    }

    return [{ label: 'Mark it done', cmd: command.task(line.ref, 'done', line.project), onpress: () => studio.taskState(line, 'done') }];
  }

  if (line.kind === 'session') {
    // Its question is answered in its terminal: the agent says that it is
    // waiting, never what the choices are, so none of them is a button.
    return [{ label: line.waiting ? 'Go to the question' : 'Open the session', cmd: 'opens here · changes nothing', changes: false, onpress: () => go('session', line.ref) }];
  }

  return [];
}
