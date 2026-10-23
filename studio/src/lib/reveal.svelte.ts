// The one way Studio jumps: reveal() asks the table in reveal.ts where the
// current mode shows a thing and goes there. A test fails if anything else
// sends the address to a session, so a new button cannot bring back the old
// jump that threw away the page beside the terminals.

import { placement, type Target } from './reveal';
import { router } from './router.svelte';
import { studio } from './state.svelte';

/** A session just started, which the deck places by the launch rule once it is listed. */
class DeckLaunch {
  id = $state<string | null>(null);
}

export const deckLaunch = new DeckLaunch();

export function reveal(target: Target): void {
  const jump = placement(studio.mode, target, router.route);

  if (target.kind === 'session' && target.launched && studio.mode === 'terminals') {
    deckLaunch.id = target.id;
  }

  if (jump.focusing) {
    studio.focusing = jump.focusing;
  }

  router.goWith(jump.parts, jump.query);
}

/** A jump given as an address's parts, as the palette and the actions hand them over. */
export function revealParts(...parts: string[]): void {
  const [first, ...rest] = parts;

  if (first === 'session' && rest[0]) {
    reveal({ kind: 'session', id: rest[0] });
  } else if (first === 'teams' && rest[0] === '+team' && rest[1]) {
    reveal({ kind: 'team', name: rest[1] });
  } else if (first === 'teams' && rest[0] && !rest[0].startsWith('+') && rest.length === 1) {
    reveal({ kind: 'run', id: rest[0] });
  } else {
    reveal({ kind: 'page', parts });
  }
}
