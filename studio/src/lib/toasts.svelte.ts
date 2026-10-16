// What just happened, top right, at one of four levels. Done and For your
// information fold into a chip after the fold time; Needs you and Problem
// stay open until answered or acknowledged. Nothing folds while the pointer
// or keyboard is on the stack, so nothing moves under the person.

export type ToastLevel = 'done' | 'info' | 'needs' | 'problem';

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
  born: number;
  folded: boolean;
  settled: number | null;
  fresh: boolean;
}

export const LEVELS: Record<ToastLevel, [string, string]> = {
  done: ['✓', 'Done'],
  info: ['↻', 'For your information'],
  needs: ['!', 'Needs you'],
  problem: ['✕', 'Problem'],
};

/** How many seconds a Done or a notice stays open before it folds. */
export const FOLD_AFTER = 6;

export class ToastStack {
  list = $state<Toast[]>([]);
  /** What a screen reader is told, politely and at once. */
  polite = $state('');
  alert = $state('');
  hold = $state(false);

  #seq = 0;
  #now = 0;

  /** Adds one at the top of the stack. */
  add(toast: { level: ToastLevel; text: string; cmd?: string; actions?: ToastAction[] }): number {
    const added: Toast = {
      id: ++this.#seq,
      level: toast.level,
      text: toast.text,
      cmd: toast.cmd ?? '',
      actions: toast.actions ?? [],
      born: this.#now,
      folded: false,
      settled: null,
      fresh: true,
    };

    this.list = [added, ...this.list.map((one) => ({ ...one, fresh: false }))];

    if (added.level === 'problem') {
      this.alert = `Problem: ${added.text}`;
    } else {
      this.polite = `${LEVELS[added.level][1]}: ${added.text}`;
    }

    return added.id;
  }

  /** Answered or acknowledged: it shows as done, with these words, and folds in turn. */
  settle(id: number, words?: string): void {
    this.list = this.list.map((one) => (one.id === id ? { ...one, settled: this.#now, text: words || one.text, actions: [], fresh: false } : one));
    this.polite = words ?? '';
  }

  /** Opens a folded one again, for another fold time. */
  open(id: number): void {
    this.list = this.list.map((one) =>
      one.id === id ? { ...one, folded: false, born: this.#now, settled: one.settled === null ? null : this.#now } : one,
    );
  }

  dismiss(id: number): void {
    this.list = this.list.filter((one) => one.id !== id);
  }

  /** One second passes. Called by the component's clock, and by the tests. */
  tick(): void {
    if (this.hold) {
      return;
    }

    this.#now += 1;
    const now = this.#now;

    let next = this.list.map((one) => {
      const age = now - one.born;
      const settledAge = one.settled === null ? -1 : now - one.settled;
      const folds = one.level === 'done' || one.level === 'info' ? age >= FOLD_AFTER : settledAge >= FOLD_AFTER;

      return folds ? { ...one, folded: true, fresh: false } : one;
    });

    next = next.filter((one) => {
      if (one.level === 'done') {
        return now - one.born < 60;
      }

      if (one.level === 'needs' || one.level === 'problem') {
        return one.settled === null || now - one.settled < 60;
      }

      return true;
    });

    // The newest three notices stay as chips; older ones go.
    const folded = next.filter((one) => one.level === 'info' && one.folded);

    if (folded.length > 3) {
      const gone = new Set(folded.slice(3).map((one) => one.id));
      next = next.filter((one) => !gone.has(one.id));
    }

    this.list = next;
  }
}

/** The one stack the whole of Studio adds to. */
export const toasts = new ToastStack();
