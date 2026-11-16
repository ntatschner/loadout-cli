// What just happened, at one of four levels. Done and For your information
// fold into a chip after OPEN_FOR seconds and leave after GONE_AFTER; Needs
// you and Problem stay open until answered or acknowledged, and then follow
// the same times. Each toast counts its own seconds, so the one under the
// pointer or holding the keyboard stops while the rest go on. Whatever
// leaves goes into the history, so a short life loses nothing.

export type ToastLevel = 'done' | 'info' | 'needs' | 'problem';

/**
 * Where a toast would be in the way: a working view, one with a terminal or
 * a split in it (the deck, a page beside it or the board, a session's page,
 * the board, Pure Focus). There every toast arrives as a chip and nothing
 * opens as a card by itself. Null is every other page.
 */
export type ToastPlace = 'working' | null;

/**
 * How notifications show in a working view, the studio-notices-working
 * setting: strip, a lane of its own under the mode bar; chips, the floating
 * stack with everything folded; off, the lane with its counts and nothing
 * else.
 */
export type NoticesWorking = 'strip' | 'chips' | 'off';
export const NOTICES_WORKING: NoticesWorking[] = ['strip', 'chips', 'off'];

/** The setting as kept, with anything unknown read as the default. */
export function noticesWorking(value: string | null | undefined): NoticesWorking {
  return NOTICES_WORKING.includes(value as NoticesWorking) ? (value as NoticesWorking) : 'strip';
}

/** What a toast is about, when it is about one session or one run: its pane or card is marked. */
export interface ToastSubject {
  kind: 'session' | 'run';
  ref: string;
}

export interface ToastAction {
  label: string;
  cmd: string;
  kind?: '' | 'needs' | 'danger';
  /** Does the thing; returns the words to settle the toast with, or nothing to use the label. */
  onpress?: () => string | void | Promise<string | void>;
}

export interface Toast {
  id: number;
  level: ToastLevel;
  text: string;
  cmd: string;
  actions: ToastAction[];
  /** Seconds counted towards folding and leaving; held toasts and open questions do not count. */
  aged: number;
  folded: boolean;
  settled: boolean;
  fresh: boolean;
  /** How many times the same Done or notice arrived while this one was showing. */
  count: number;
  /** The session or run it is about, if it is about one. */
  subject?: ToastSubject;
}

/** One that has left the stack, for the session's history. */
export interface ToastGone {
  id: number;
  level: ToastLevel;
  text: string;
  cmd: string;
  count: number;
}

export const LEVELS: Record<ToastLevel, [string, string]> = {
  done: ['✓', 'Done'],
  info: ['↻', 'For your information'],
  needs: ['!', 'Needs you'],
  problem: ['✕', 'Problem'],
};

/** How many seconds a Done, a notice or an answered question stays open before it folds. */
export const OPEN_FOR = 4;
/** How many seconds it is kept at all, chip included, before it goes into the history. */
export const GONE_AFTER = 10;
/** How many of those the history keeps. */
export const HISTORY_KEPT = 50;

/** The level a toast shows as: an answered question reads as Done. */
export function shownLevel(toast: Toast): ToastLevel {
  return toast.settled ? 'done' : toast.level;
}

/** Whether its seconds count: a question waiting on someone never leaves by itself. */
function timed(toast: Toast): boolean {
  return toast.level === 'done' || toast.level === 'info' || toast.settled;
}

export class ToastStack {
  list = $state<Toast[]>([]);
  history = $state<ToastGone[]>([]);
  /** What a screen reader is told, politely and at once. */
  polite = $state('');
  alert = $state('');
  /** The toast under the pointer, and the one holding the keyboard: their clocks stop. */
  pointed = $state<number | null>(null);
  focused = $state<number | null>(null);
  /** In one of the named places every toast arrives as a chip. */
  tucked = $state(false);

  #seq = 0;

  /** Adds one at the top of the stack, or counts one more of the same Done or notice. */
  add(toast: { level: ToastLevel; text: string; cmd?: string; actions?: ToastAction[]; subject?: ToastSubject }): number {
    const cmd = toast.cmd ?? '';
    const same = toast.level === 'done' || toast.level === 'info'
      ? this.list.find((one) => one.level === toast.level && !one.settled && one.text === toast.text && one.cmd === cmd)
      : undefined;

    if (same) {
      this.list = this.list.map((one) => (one.id === same.id ? { ...one, count: one.count + 1, aged: 0 } : one));
      this.polite = `${LEVELS[toast.level][1]}: ${toast.text}, ${same.count + 1} times`;

      return same.id;
    }

    const added: Toast = {
      id: ++this.#seq,
      level: toast.level,
      text: toast.text,
      cmd,
      actions: toast.actions ?? [],
      aged: 0,
      folded: this.tucked,
      settled: false,
      fresh: true,
      count: 1,
      ...(toast.subject ? { subject: toast.subject } : {}),
    };

    this.list = [added, ...this.list.map((one) => ({ ...one, fresh: false }))];

    if (added.level === 'problem') {
      this.alert = `Problem: ${added.text}`;
    } else {
      this.polite = `${LEVELS[added.level][1]}: ${added.text}`;
    }

    return added.id;
  }

  /** Answered or acknowledged: it shows as done, with these words, and from now on follows Done's times. */
  settle(id: number, words?: string): void {
    this.list = this.list.map((one) => (one.id === id ? { ...one, settled: true, aged: 0, text: words || one.text, actions: [], fresh: false } : one));
    this.polite = words ?? '';

    // The button that answered it is gone, and a removed element may never
    // say it lost focus, which would hold this one for good.
    if (this.focused === id) {
      this.focused = null;
    }
  }

  /** Opens a folded one again, for another run of the times. */
  open(id: number): void {
    this.list = this.list.map((one) => (one.id === id ? { ...one, folded: false, aged: 0 } : one));

    // The chip that had the keyboard has just been replaced by the card.
    if (this.focused === id) {
      this.focused = null;
    }
  }

  dismiss(id: number): void {
    this.#leave(this.list.filter((one) => one.id === id));
  }

  /**
   * Entering a named place folds everything to a chip. Leaving it opens
   * again whatever still needs someone, where it is, so nothing that needs
   * you stays hidden.
   */
  tuck(tucked: boolean): void {
    if (tucked === this.tucked) {
      return;
    }

    this.tucked = tucked;
    this.list = this.list.map((one) => {
      if (tucked) {
        return { ...one, folded: true, fresh: false };
      }

      return timed(one) ? one : { ...one, folded: false, fresh: false };
    });
  }

  /**
   * The key: folds whatever is open; with nothing open, sends everything not
   * waiting on someone into the history. A question waiting on you stays.
   */
  sweep(): void {
    if (this.list.some((one) => !one.folded)) {
      this.list = this.list.map((one) => ({ ...one, folded: true, fresh: false }));

      return;
    }

    this.#leave(this.list.filter(timed));
  }

  /** How many of each level are in the stack now, as it shows them: what the strip's badges count. */
  get counts(): Record<ToastLevel, number> {
    const counts: Record<ToastLevel, number> = { done: 0, info: 0, needs: 0, problem: 0 };

    for (const one of this.list) {
      counts[shownLevel(one)] += 1;
    }

    return counts;
  }

  /**
   * The Needs you and Problems still open about one session or run, the
   * most pressing first: what its pane's mark shows. Answered ones read as
   * Done and are not counted.
   */
  about(kind: ToastSubject['kind'], ref: string): { level: 'needs' | 'problem'; count: number } | null {
    let needs = 0;
    let problems = 0;

    for (const one of this.list) {
      if (one.subject?.kind !== kind || one.subject.ref !== ref) {
        continue;
      }

      const shown = shownLevel(one);

      if (shown === 'problem') {
        problems += 1;
      } else if (shown === 'needs') {
        needs += 1;
      }
    }

    if (problems) {
      return { level: 'problem', count: problems + needs };
    }

    return needs ? { level: 'needs', count: needs } : null;
  }

  clearHistory(): void {
    this.history = [];
  }

  /** One second passes. Called by the component's clock, and by the tests. */
  tick(): void {
    const gone: Toast[] = [];
    const next: Toast[] = [];

    for (const one of this.list) {
      if (!timed(one) || one.id === this.pointed || one.id === this.focused) {
        next.push(one);
        continue;
      }

      const aged = one.aged + 1;

      if (aged >= GONE_AFTER) {
        gone.push(one);
      } else {
        next.push({ ...one, aged, folded: one.folded || aged >= OPEN_FOR, fresh: aged >= OPEN_FOR ? false : one.fresh });
      }
    }

    this.list = next;
    this.#remember(gone);
  }

  #leave(leaving: Toast[]): void {
    const ids = new Set(leaving.map((one) => one.id));
    this.list = this.list.filter((one) => !ids.has(one.id));
    this.#remember(leaving);
  }

  #remember(leaving: Toast[]): void {
    if (!leaving.length) {
      return;
    }

    for (const one of leaving) {
      if (this.pointed === one.id) {
        this.pointed = null;
      }

      if (this.focused === one.id) {
        this.focused = null;
      }
    }

    const kept = leaving.map((one) => ({ id: one.id, level: shownLevel(one), text: one.text, cmd: one.cmd, count: one.count }));
    this.history = [...kept, ...this.history].slice(0, HISTORY_KEPT);
  }
}

/** The one stack the whole of Studio adds to. */
export const toasts = new ToastStack();
