<script lang="ts">
  // Terminal Focus: Home, Sessions and a session's own page become the
  // deck, every hosted session a tab and the picked ones live in panes.
  // Loaded only when first needed, like the session page, since the
  // terminal is most of Studio's size. The layout and the last session
  // picked are kept in this browser.
  import SessionPane from '../lib/components/SessionPane.svelte';
  import TermDeck from '../lib/components/TermDeck.svelte';
  import { studio } from '../lib/state.svelte';
  import { wordFor } from '../lib/signals';
  import type { DeckLayout, DeckSession } from '../lib/modes';

  let { id = null, onlaunch = undefined }: { id?: string | null; onlaunch?: () => void } = $props();

  const LAYOUT = 'studio-deck-layout';
  const LAST = 'studio-deck-session';

  const kept = Number(studio.kept(LAYOUT, '2'));
  const layout: DeckLayout = kept === 1 || kept === 4 ? kept : 2;

  // When each session's output was last on the screen, for the new-output dot.
  let seen = $state<Record<string, number>>({});
  let shownNow = $state<string | null>(null);
  const openedAt = Date.now();

  const lines = $derived(studio.lines.filter((line) => line.kind === 'session'));
  const sessions: DeckSession[] = $derived(
    lines.map((line) => {
      const wrote = line.session?.lastOutputAt ? Date.parse(line.session.lastOutputAt) : 0;

      return {
        id: line.ref,
        title: line.title,
        word: line.stage === 4 ? 'ended' : wordFor(line, studio.idleAfter),
        where: line.session?.project ?? null,
        agent: line.session?.kind === 'command' ? 'loadout' : (line.session?.agent ?? null),
        unread: line.ref !== shownNow && wrote > (seen[line.ref] ?? openedAt),
      };
    }),
  );

  // svelte-ignore state_referenced_locally
  const start = id ?? studio.kept(LAST, '');

  /** Output on the screen counts as seen; kept coarse, so a busy agent does not redraw the tabs per line. */
  function looked(one: string) {
    const now = Date.now();

    if (now - (seen[one] ?? 0) > 5000) {
      seen = { ...seen, [one]: now };
    }
  }

  function picked(one: string) {
    shownNow = one;
    seen = { ...seen, [one]: Date.now() };
    studio.keep(LAST, one);
  }
</script>

<h1 class="ls-vh">Terminals</h1>
<div class="deckwrap">
  <TermDeck
    {sessions}
    current={id ?? (start || null)}
    {layout}
    onpick={picked}
    onlayout={(next) => studio.keep(LAYOUT, String(next))}
    onnew={studio.watchOnly ? undefined : onlaunch}
  >
    {#snippet pane(one, register)}
      <SessionPane id={one} onready={register} onoutput={() => looked(one)} />
    {/snippet}
  </TermDeck>
</div>
