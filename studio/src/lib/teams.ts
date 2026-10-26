// Team runs as the Teams components draw them: what each kind of question
// offers as answers and the exact command each answer runs, the spend line,
// and the reasons a run wants you. Ported from the design system's bundle.js
// (Gate, Spend, NeedsYou). Kept apart from the components so what a press
// runs can be checked without a page.
import type { PermissionParts } from './api';
import { quoted } from './commands';

/**
 * The kinds of question a run stops to ask. A kind this does not know is
 * answered yes or no, as a confirmation is, which is what the daemon falls
 * back to as well.
 */
export type GateKind = 'question' | 'confirm' | 'permission' | 'outward' | 'merge' | 'brief' | 'readings' | 'proposal' | 'budget';

export const GATE_WORDS: Record<GateKind, string> = {
  confirm: 'asks you to confirm',
  question: 'the lead asks',
  permission: 'asks permission',
  outward: 'asks to reach outside this machine',
  merge: 'asks to merge',
  brief: 'a brief, before it goes out',
  readings: 'before any worker starts',
  proposal: 'what the run will be judged on',
  budget: 'it has spent its budget',
};

const KNOWN = new Set<string>(Object.keys(GATE_WORDS));

/** A kind as the Gate draws it: the daemon's word where the Gate knows it, otherwise a yes-or-no confirmation. */
export function gateKind(kind: string | null | undefined): GateKind {
  if (kind === 'done-when' || kind === 'donewhen') {
    return 'proposal';
  }

  return kind && KNOWN.has(kind) ? (kind as GateKind) : 'confirm';
}

export interface GateAsk {
  kind: GateKind;
  run: string;
  gate?: string | null;
  question: string;
  options?: string[];
  recommendation?: string | null;
  /** brief: the lead's brief, as it would go out. */
  brief?: string;
  /** proposal: the lead's proposed criteria. */
  proposal?: string[];
}

/** What a press sends: the answer, and for a brief or a proposal what to send instead; or a request to think again. */
export interface GateAnswer {
  answer: string;
  instead?: string | null;
  reason?: string | null;
  thinkAgain?: boolean;
}

export interface GateButton {
  label: string;
  kind: '' | 'needs';
  solid: boolean;
  glyph: string;
  /** The command it runs, exactly. */
  cmd: string;
  /** What the run is told, once it is sent. */
  said: string;
  send: GateAnswer;
}

/** `$ loadout team gate <run> --gate <id> <rest> --by dashboard`. */
export function gateLine(run: string, gate: string | null | undefined, rest: string): string {
  return `$ loadout team gate ${run}${gate ? ` --gate ${gate}` : ''} ${rest} --by dashboard`;
}

/**
 * The answers a question offers and the command each runs.
 *
 * @param typed  the brief or the criteria as they are in the box now
 * @param figure a budget the person typed, or empty
 * @param reason why, in a sentence the journal keeps, or empty
 */
export function gateButtons(ask: GateAsk, typed = '', figure = '', reason = ''): GateButton[] {
  const why = reason.trim() ? ` --reason ${quoted(reason.trim())}` : '';
  const line = (rest: string) => gateLine(ask.run, ask.gate, rest);
  const buttons: GateButton[] = [];

  switch (ask.kind) {
    case 'question':
    case 'readings': {
      const options = ask.kind === 'readings' ? ['Accept these readings'] : (ask.options ?? []);

      options.forEach((option, index) =>
        buttons.push({
          label: option,
          kind: 'needs',
          // The lead's recommendation is the solid one; a reading has one answer, and it is that.
          solid: option === (ask.recommendation ?? (ask.kind === 'readings' ? option : null)),
          glyph: options.length > 1 ? String(index + 1) : '✓',
          cmd: line(`--answer ${quoted(option)}`) + why,
          said: `You answered: ${option}.`,
          send: { answer: option, reason: reason.trim() || null },
        }),
      );

      buttons.push({
        label: 'Think again',
        kind: '',
        solid: false,
        glyph: '↻',
        // The same line as command.thinkAgain, which StudioBuildTests checks against the daemon.
        cmd: line('--think-again') + why,
        said: ask.kind === 'readings'
          ? 'Sent back. No worker is briefed this round; the lead restates its readings and asks again.'
          : 'Sent back to the lead to decide itself or ask a better question.',
        send: { answer: 'Think again', reason: reason.trim() || null, thinkAgain: true },
      });

      return buttons;
    }

    case 'brief': {
      const changed = typed.trim() !== (ask.brief ?? '').trim();

      return [
        {
          label: changed ? 'Send your brief' : 'Send it',
          kind: 'needs',
          solid: true,
          glyph: '✓',
          cmd: line(`--answer yes${changed ? ` --instead ${quoted(typed.trim())}` : ''}`),
          said: changed ? 'Sent your brief instead of the lead’s.' : 'Sent the lead’s brief.',
          send: { answer: 'yes', instead: changed ? typed.trim() : null },
        },
        {
          label: 'Don’t send it',
          kind: '',
          solid: false,
          glyph: '✕',
          cmd: line('--answer no'),
          said: 'Not sent. The lead is told.',
          send: { answer: 'no' },
        },
      ];
    }

    case 'proposal': {
      const lines = typed.split('\n').map((one) => one.trim()).filter(Boolean);
      const same = lines.join('\n') === (ask.proposal ?? []).join('\n');

      return [
        {
          label: lines.length ? 'Hold it to these' : 'Hold it to nothing',
          kind: 'needs',
          solid: true,
          glyph: '✓',
          cmd: line(`--answer yes${same ? '' : ` --instead ${quoted(lines.join('\n'))}`}`),
          said: lines.length
            ? `The run is held to ${lines.length} ${lines.length === 1 ? 'criterion' : 'criteria'}.`
            : 'The run carries on with nothing checking it.',
          send: { answer: 'yes', instead: same ? null : lines.join('\n') },
        },
      ];
    }

    case 'budget': {
      (ask.options ?? []).forEach((option, index) =>
        buttons.push({
          label: option,
          kind: 'needs',
          solid: option === ask.recommendation,
          glyph: String(index + 1),
          cmd: line(`--answer ${quoted(option)}`),
          said: `You answered: ${option}.`,
          send: { answer: option },
        }),
      );

      if (figure.trim()) {
        const value = figure.trim();

        buttons.push({
          label: /^none$/i.test(value) ? 'No cap' : `Raise it to $${value.replace(/^\$/, '')}`,
          kind: '',
          solid: false,
          glyph: '$',
          cmd: line(`--answer ${quoted(value)}`),
          said: `You set the budget to ${value}.`,
          send: { answer: value },
        });
      }

      return buttons;
    }

    default:
      // confirm, permission, outward, merge, and any kind this does not know.
      return [
        { label: 'Yes', kind: 'needs', solid: false, glyph: '✓', cmd: line('--answer yes') + why, said: 'You said yes.', send: { answer: 'yes', reason: reason.trim() || null } },
        {
          label: 'No',
          kind: '',
          solid: false,
          glyph: '✕',
          cmd: line('--answer no') + why,
          said: 'You said no. The run is told and goes on without it.',
          send: { answer: 'no', reason: reason.trim() || null },
        },
      ];
  }
}

/** Dollars as the pages write them. */
export const money = (amount: number | null | undefined) => `$${Number(amount ?? 0).toFixed(2)}`;

/** Every reason a run wants you, and when each clears itself. */
export const NEED_REASONS = {
  asked: { word: 'waiting for you', text: 'It stopped and asked you something.', clears: 'you answer it' },
  silent: { word: 'needs a decision', text: 'The lead took a round without asking for anything. Another one ends the run.', clears: 'it asks for a node, or finishes' },
  quiet: { word: 'quiet', text: '{node} has said nothing for {for}, three times its usual turn.', clears: 'it says anything at all' },
  budget: { word: 'projected over', text: 'It is projected to overrun its budget: {detail}.', clears: 'it slows, finishes, or you stop it' },
  spent: { word: 'limited', text: 'It has spent its budget.', clears: 'you raise it, or it stops' },
  approval: { word: 'needs approval', text: 'A trigger rule wants to start a run that writes.', clears: 'you approve or reject it' },
  paused: { word: 'paused', text: 'It reached one of its caps and starts nothing until you resume it.', clears: 'you resume it' },
  stale: { word: 'held too long', text: 'It held events past the 24-hour window.', clears: 'you release them, or they are dropped after a week' },
  source: { word: 'stopped', text: 'A trigger source has stopped.', clears: 'what stopped it is put right' },
} as const;

export type NeedReason = keyof typeof NEED_REASONS;

/** A button a Teams component shows: what it says, the command it runs, and what pressing it does. */
export interface Act {
  label: string;
  kind?: '' | 'needs' | 'danger';
  solid?: boolean;
  glyph?: string;
  cmd?: string;
  disabledReason?: string;
  onpress?: () => void;
}

/** What a Gate is given, less the run it belongs to. */
export interface GateShown {
  kind: GateKind;
  gate?: string | null;
  node?: string | null;
  asked?: string | null;
  question: string;
  options?: string[];
  recommendation?: string | null;
  timer?: string | null;
  readings?: [string, string][];
  brief?: string;
  proposal?: string[];
  held?: string[];
  /** A permission's parts, drawn in place of the question where present. */
  ask?: PermissionParts | null;
  disabledReason?: string;
  onanswer?: (answer: GateAnswer) => Promise<boolean | void> | boolean | void;
}

/** A run as a card or a board row shows it. */
export interface RunSummary {
  id: string;
  goal: string;
  team: string;
  project?: string | null;
  posture?: string | null;
  word: string;
  round?: number;
  rounds?: number;
  elapsed?: string | null;
  spent?: number;
  cap?: number | null;
  uncapped?: boolean;
  projected?: number | null;
  rate?: string | null;
}

/** One reason a run wants you, as Needs you lists it. */
export interface NeedItem {
  reason: NeedReason;
  run: string;
  goal: string;
  team?: string;
  href?: string;
  node?: string;
  quiet?: string;
  detail?: string;
  text?: string;
  clears?: string;
  gate?: GateShown | null;
  actions?: Act[];
}

/** Clearing out runs: which, by ending, age and how many of the newest to keep. */
export interface Prune {
  /** Empty for any ending. */
  outcome: string;
  /** "30d", "12h"; empty for any age. */
  olderThan: string;
  /** Never fewer than the newest this many; empty for no floor. */
  keep: string;
}

/** `$ loadout team runs prune …`, the line Clear out runs runs. */
export function pruneCommand(p: Prune, dry = false): string {
  return '$ loadout team runs prune'
    + (p.outcome === 'failed' ? ' --failed' : p.outcome ? ` --outcome ${p.outcome}` : '')
    + (p.olderThan ? ` --older-than ${p.olderThan}` : '')
    + (p.keep ? ` --keep ${p.keep}` : '')
    + (dry ? ' --dry-run' : ' --yes');
}

/** What Clear out runs would do, said back as a sentence before it goes; null when it has been told nothing. */
export function pruneSentence(p: Prune): string | null {
  if (!p.outcome && !p.olderThan && !p.keep) {
    return null;
  }

  return `Moves ${p.outcome ? `the ${p.outcome} runs` : 'the runs'}${p.olderThan ? ` older than ${p.olderThan}` : ''} to the bin${p.keep ? `, never the newest ${p.keep}` : ''}, and never one still going or one that left a branch nothing merged.`;
}

/** The five depths of a run, on keys 1 to 5. */
export const DEPTHS = ['What it said', 'What it cost', 'The papers', 'What it changed', 'What it delivered'] as const;

/**
 * The depth a key press picks, or null. A key typed into a field, a dialog or
 * a terminal is that thing's own, and a modified key is somebody else's.
 */
export function depthKey(event: KeyboardEvent): number | null {
  if (event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) {
    return null;
  }

  const target = event.target as HTMLElement | null;
  const tag = target?.tagName;

  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target?.isContentEditable || target?.closest?.('.ls-term, .xterm, [role=dialog], [role=alertdialog]')) {
    return null;
  }

  const index = '12345'.indexOf(event.key);

  return index < 0 || event.key.length !== 1 ? null : index;
}

/** One line of a run's journal or a node's stream. */
export interface JournalLine {
  time: string;
  text: string;
  family?: string;
  kind?: string;
  /** Said by a sub-agent the node started. */
  sub?: boolean;
  repeats?: number;
}
