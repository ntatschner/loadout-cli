<script lang="ts">
  // Terminal Focus: Home, Sessions and a session's own page become the
  // deck, every hosted session a tab and the picked ones live in panes.
  // Loaded only when first needed, like the session page, since the
  // terminal is most of Studio's size. The layout, the session in each pane,
  // where the dividers sit, the last session picked, and how the tabs are
  // arranged, hidden and cleared are kept in this browser; a tab's name is
  // the daemon's, so every browser shows it.
  import { untrack } from 'svelte';
  import SessionPane from '../lib/components/SessionPane.svelte';
  import TermDeck from '../lib/components/TermDeck.svelte';
  import { studio } from '../lib/state.svelte';
  import { deckLaunch } from '../lib/reveal.svelte';
  import { wordFor } from '../lib/signals';
  import { TABS_KEY, arrange, clear, hide, prune, readTabs, unhide, withOrder, type TabState } from '../lib/tabs';
  import { readSplit, SPLIT_KEYS } from '../lib/splits';
  import type { DeckLayout, DeckSession, DeckSlots } from '../lib/modes';

  let { id = null, onlaunch = undefined }: { id?: string | null; onlaunch?: () => void } = $props();

  const LAYOUT = 'studio-deck-layout';
  const GROWN = 'studio-deck-grown';
  const LAST = 'studio-deck-session';
  const SLOTS = 'studio-deck-slots';
  const splits = { 2: readSplit(studio.kept(SPLIT_KEYS.deck2, ''), 1), 4: readSplit(studio.kept(SPLIT_KEYS.deck4, ''), 2) };

  const kept = Number(studio.kept(LAYOUT, '2'));
  const layout: DeckLayout = kept === 1 || kept === 4 ? kept : 2;
  // Panes a launch added past that layout, so going to another page and back keeps them.
  const grownKept = Number(studio.kept(GROWN, '0'));
  const grown: DeckLayout | null = (grownKept === 2 || grownKept === 4) && grownKept > layout ? grownKept : null;
  const slots = keptSlots();

  /** The panes as they were; anything unreadable is no arrangement at all. Never more than four, the most a layout shows. */
  function keptSlots(): DeckSlots {
    try {
      const read: unknown = JSON.parse(studio.kept(SLOTS, '[]'));

      return Array.isArray(read) ? read.slice(0, 4).map((one) => (typeof one === 'string' && one ? one : null)) : [];
    } catch {
      return [];
    }
  }

  // When each session's output was last on the screen, for the new-output dot.
  let seen = $state<Record<string, number>>({});
  let shownNow = $state<string | null>(null);
  const openedAt = Date.now();

  const lines = $derived(studio.lines.filter((line) => line.kind === 'session'));
  const all: DeckSession[] = $derived(
    lines.map((line) => {
      const wrote = line.session?.lastOutputAt ? Date.parse(line.session.lastOutputAt) : 0;

      return {
        id: line.ref,
        title: line.title,
        word: line.stage === 4 ? 'ended' : wordFor(line, studio.idleAfter),
        where: line.session?.project ?? null,
        agent: line.session?.kind === 'command' ? 'loadout' : (line.session?.agent ?? null),
        unread: line.ref !== shownNow && wrote > (seen[line.ref] ?? openedAt),
        ended: line.stage === 4 || !!line.session?.ended,
        launchTitle: line.session?.launchTitle ?? line.title,
        namesAgent: !!line.session?.namesAgent,
        kind: line.session?.kind ?? 'launch',
        project: line.session?.project ?? null,
      };
    }),
  );

  let tabs = $state<TabState>(readTabs(studio.kept(TABS_KEY, '')));
  const arranged = $derived(arrange(all, tabs));

  function keepTabs(next: TabState) {
    tabs = next;
    studio.keep(TABS_KEY, JSON.stringify(next));
  }

  // A session the address names is shown, even one hidden here before, and
  // stays shown.
  $effect(() => {
    const named = id;

    untrack(() => {
      if (named && tabs.hidden.includes(named)) {
        keepTabs(unhide(tabs, named));
      }
    });
  });

  // Once the daemon has said what it hosts, ids it no longer has are let go.
  $effect(() => {
    const present = studio.loaded ? studio.sessions.map((one) => one.id) : null;

    untrack(() => {
      if (!present) {
        return;
      }

      const pruned = prune(tabs, present);

      if (JSON.stringify(pruned) !== JSON.stringify(tabs)) {
        keepTabs(pruned);
      }
    });
  });

  const hosted = (one: string) => studio.sessions.find((session) => session.id === one);

  // svelte-ignore state_referenced_locally
  const start = id ?? studio.kept(LAST, '');

  /** Output on the screen counts as seen; kept coarse, so a busy agent does not redraw the tabs per line. */
  function looked(one: string) {
    const now = Date.now();

    if (now - (seen[one] ?? 0) > 5000) {
      seen = { ...seen, [one]: now };
    }

    stir(one);
  }

  // When each session last wrote or was typed into here, for which pane a
  // launch takes when four are full. Studio's own: a reload starts them level.
  let active = $state<Record<string, number>>({});

  function stir(one: string) {
    const now = Date.now();

    if (now - (active[one] ?? 0) > 5000) {
      active = { ...active, [one]: now };
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
    sessions={arranged.shown}
    hidden={arranged.hidden}
    changes={!studio.watchOnly}
    current={id ?? (start || null)}
    {layout}
    {grown}
    {slots}
    listed={studio.loaded}
    launch={deckLaunch.id}
    {active}
    onlaunched={() => (deckLaunch.id = null)}
    onpick={picked}
    onlayout={(next) => { studio.keep(LAYOUT, String(next)); studio.keep(GROWN, '0'); }}
    ongrown={(panes) => studio.keep(GROWN, String(panes))}
    onslots={(next) => studio.keep(SLOTS, JSON.stringify(next))}
    {splits}
    onsplit={(count, fractions) => studio.keep(count === 2 ? SPLIT_KEYS.deck2 : SPLIT_KEYS.deck4, JSON.stringify(fractions))}
    onnew={studio.watchOnly ? undefined : onlaunch}
    onmove={(order) => keepTabs(withOrder(tabs, order))}
    onrename={(one, name) => { const session = hosted(one); return session && studio.renameSession(session, name); }}
    onhide={(one) => keepTabs(hide(tabs, one))}
    onunhide={(one) => keepTabs(unhide(tabs, one))}
    onclear={(one) => keepTabs(clear(tabs, one))}
    onend={(one) => { const session = hosted(one); return session && studio.stopSession(session); }}
  >
    {#snippet pane(one, register)}
      <SessionPane id={one} onready={register} onoutput={() => looked(one)} ontyped={() => stir(one)} />
    {/snippet}
  </TermDeck>
</div>
