// The "Before this" band's reasoning, apart from the page so it can be tested:
// which offer a key means, what the stack key moves to, and the exact words run.

export interface Offer {
  check: string;
  name: string;
  state: string;
  evidence: string;
  canOffer: boolean;
  stacks: string[];
  environments: string[];
  prerequisite: string | null;
  linked: boolean;
}

export interface OfferSheet {
  project: string;
  task: string;
  built: string;
  github: string;
  offers: Offer[];
  unseen: { check: string; state: string; hint: string }[];
  notNeeded: string[];
  notes: string[];
  links?: { check: string; prerequisite: string; outgrown: boolean }[];
}

export interface GapCount {
  seen: number;
  noOffer: number;
  unseen: number;
}

/** The offer one keystroke takes: the first that can be taken and is still in front of the person. */
export function firstOpen(offers: Offer[], later: string[]): Offer | null {
  return offers.find((offer) => offer.canOffer && !offer.linked && !later.includes(offer.check)) ?? null;
}

/** The stack choice in force for an offer: what the person cycled to, or the default. */
export function stackIn(offer: Offer, chosen: Record<string, string>): string | null {
  return chosen[offer.check] ?? offer.stacks[0] ?? null;
}

/** The choice after this one, coming back round to the first. */
export function nextStack(offer: Offer, chosen: Record<string, string>): string | null {
  if (offer.stacks.length === 0) {
    return null;
  }

  const at = offer.stacks.indexOf(stackIn(offer, chosen) ?? '');

  return offer.stacks[(at + 1) % offer.stacks.length];
}

/** A stack choice as somebody would read it. */
export function stackWord(stack: string): string {
  return stack === 'all' ? 'all detected stacks' : stack === 'generic' ? 'no particular stack' : stack;
}

/** Environment names as typed: split on commas or spaces, with the empties gone. */
export function environmentNames(typed: string): string[] {
  return typed
    .split(/[\s,]+/)
    .map((name) => name.trim())
    .filter(Boolean);
}

/**
 * The words after 'task offers' that take an offer up. A default is left out,
 * so the line shown is the shortest that does it, and the options are joined
 * with '=' because the parser refuses a separate value starting with a dash.
 */
export function acceptOfferWords(id: string, where: string[], offer: Offer, chosen: Record<string, string>, typed?: string): string[] {
  const stack = stackIn(offer, chosen);
  const names = typed === undefined ? [] : environmentNames(typed);
  const changedNames = names.length > 0 && names.join(' ') !== offer.environments.join(' ');

  return [
    id,
    ...where,
    `--accept=${offer.check}`,
    ...(stack && stack !== offer.stacks[0] ? [`--stack=${stack}`] : []),
    ...(changedNames ? names.map((name) => `--env-name=${name}`) : []),
  ];
}

/** What the shelf says of an idea's gaps, or null with nothing seen. A check nobody could look at is never counted. */
export function gapWord(gaps: GapCount | null | undefined): string | null {
  if (!gaps?.seen) {
    return null;
  }

  const counted = `${gaps.seen} gap${gaps.seen === 1 ? '' : 's'}`;

  return gaps.noOffer ? `${counted}, ${gaps.noOffer} no offer` : counted;
}
