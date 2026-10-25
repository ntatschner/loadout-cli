// What the Teams pages do to a run, each through the daemon's own route for
// it and each said back with the command it runs: answering a question,
// holding, stopping, changing what it may spend, saying something to the
// lead. The words match the classic dashboard's mapping (DashboardActions).
import * as api from './api';
import { quoted } from './commands';
import { studio } from './state.svelte';
import { gateLine, type GateAnswer } from './teams';

export function answerGate(run: string, gate: string | null | undefined, send: GateAnswer): Promise<boolean> {
  // Sending a question back is a flag, not an answer: an answer beside it is
  // passed to the command too, which refuses the pair.
  const body: Record<string, string | boolean> = send.thinkAgain ? { gate: gate ?? '', thinkAgain: true } : { gate: gate ?? '', answer: send.answer };

  if (send.instead) {
    body.instead = send.instead;
  }

  if (send.reason) {
    body.reason = send.reason;
  }

  const rest = send.thinkAgain
    ? '--think-again'
    : `--answer ${quoted(send.answer)}${send.instead ? ` --instead ${quoted(send.instead)}` : ''}`;

  return studio.perform(
    () => api.runs.act(run, 'gate', body),
    send.thinkAgain ? 'Sent back to the lead to think again' : `You answered: ${send.answer}`,
    gateLine(run, gate, rest) + (send.reason ? ` --reason ${quoted(send.reason)}` : ''),
  );
}

export const runLines = {
  hold: (run: string) => `$ loadout team halt ${run} --pause`,
  carryOn: (run: string) => `$ loadout team halt ${run} --resume`,
  stop: (run: string) => `$ loadout team halt ${run}`,
  budget: (run: string, usd: string) => `$ loadout team budget ${run} --usd ${usd || '<figure or none>'}`,
  message: (run: string, message: string) => `$ loadout team message ${run} --message ${quoted(message || '…')}`,
  pickUp: (run: string) => `$ loadout team resume ${run}`,
  forget: (run: string) => `$ loadout team runs remove ${run}`,
  close: (run: string) => `$ loadout team runs close ${run}`,
  pr: (run: string, node: string) => `$ loadout team pr ${run} --node ${node} --draft`,
};

export const hold = (run: string) => studio.perform(() => api.runs.act(run, 'pause'), 'It holds after this round', runLines.hold(run));
export const carryOn = (run: string) => studio.perform(() => api.runs.act(run, 'resume'), 'It carries on', runLines.carryOn(run));
export const stop = (run: string) => studio.perform(() => api.runs.act(run, 'stop'), 'It stops after this round', runLines.stop(run));
export const pickUp = (run: string) => studio.perform(() => api.runs.act(run, 'pickup'), 'Picked up where it stopped', runLines.pickUp(run));
export const forget = (run: string) => studio.perform(() => api.runs.act(run, 'forget'), 'Forgotten, to the bin', runLines.forget(run));
export const close = (run: string, confirmed: boolean) =>
  studio.perform(() => api.runs.act(run, 'close', { confirmed }), 'Ended: nothing was driving it', runLines.close(run) + (confirmed ? ' --yes' : ''));
export const setBudget = (run: string, usd: string) => studio.perform(() => api.runs.act(run, 'budget', { budget: usd }), `It may spend ${/^none$/i.test(usd) ? 'without a cap' : `$${usd}`}`, runLines.budget(run, usd));
export const tellLead = (run: string, message: string) => studio.perform(() => api.runs.act(run, 'message', { message }), 'Sent to the lead', runLines.message(run, message));
