<script lang="ts">
  // Terminal Focus: sessions as tabs over one, two or four panes, each pane
  // its own live terminal. Ctrl+Alt+1–9 picks a session by its tab and
  // Ctrl+Alt+arrows move between the panes shown; the picked one is always
  // among them. Panes keep their places: clicking into one only moves the
  // highlight, and a session picked from its tab takes the highlighted pane.
  // Find searches the terminal that has the keyboard. The panes themselves
  // come from the page, so this knows nothing about sockets.
  import { untrack, type Snippet } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import SessionTabs from './SessionTabs.svelte';
  import { signalOf } from '../signals';
  import { deckKey, morph, placeSlots, type DeckLayout, type DeckSession, type DeckSlots, type PaneHandle } from '../modes';

  interface Props {
    /** The tabs on the strip, in this browser's order. */
    sessions: DeckSession[];
    current?: string | null;
    layout?: DeckLayout;
    /** Which session each pane showed last time, kept by the page. */
    slots?: DeckSlots;
    /** Whether the sessions have been read yet; before then, none in a pane is said to have gone. */
    listed?: boolean;
    /** One pane's terminal: given the session and a way to hand its handle back. */
    pane: Snippet<[string, (handle: PaneHandle | null) => void]>;
    /** Running sessions this browser took off the strip. */
    hidden?: DeckSession[];
    /** Whether names can be changed and sessions ended: false where Studio is only watching. */
    changes?: boolean;
    onpick?: (id: string) => void;
    onlayout?: (layout: DeckLayout) => void;
    onslots?: (slots: DeckSlots) => void;
    onnew?: () => void;
    onmove?: (order: string[]) => void;
    onrename?: (id: string, name: string | null) => unknown;
    onhide?: (id: string) => void;
    onunhide?: (id: string) => void;
    onclear?: (id: string) => void;
    onend?: (id: string) => unknown;
  }

  let {
    sessions,
    current = null,
    layout = 1,
    slots = [],
    listed = true,
    pane,
    hidden = [],
    changes = true,
    onpick = undefined,
    onlayout = undefined,
    onslots = undefined,
    onnew = undefined,
    onmove = undefined,
    onrename = undefined,
    onhide = undefined,
    onunhide = undefined,
    onclear = undefined,
    onend = undefined,
  }: Props = $props();

  const LAYOUTS: [DeckLayout, string, string][] = [
    [1, 'One', 'M2 2h16v12H2z'],
    [2, 'Two side by side', 'M2 2h16v12H2z M10 2v12'],
    [4, 'Four', 'M2 2h16v12H2z M10 2v12 M2 8h16'],
  ];

  const panesId = `ls-deck-${Math.random().toString(36).slice(2, 7)}`;
  // The page says which to start on and where each pane was; after that the
  // deck keeps its own.
  // svelte-ignore state_referenced_locally
  let panes = $state<DeckLayout>(layout);
  // svelte-ignore state_referenced_locally
  let arranged = $state<DeckSlots>(placeSlots(slots, { order: [], layout }));
  // The highlighted pane, by its place.
  // svelte-ignore state_referenced_locally
  let hi = $state(Math.max(0, arranged.indexOf(current)));
  let host = $state<HTMLElement | undefined>();
  let narrow = $state(false);
  let term = $state('');
  let found = $state('');
  const handles = new Map<string, PaneHandle>();

  const order = $derived(sessions.map((one) => one.id));
  const on = $derived(arranged[hi] ?? order[0] ?? null);
  // Below 900px one pane, the highlighted one, with the tabs to move between
  // sessions; the wider arrangement waits for the window to widen again.
  const shown = $derived(narrow ? (on ? [on] : []) : arranged.filter((id): id is string => !!id));
  const waiting = $derived(sessions.filter((one) => signalOf(one.word).family === 'needs'));

  /** Puts a session in a pane, if it is not in one already, and highlights it. */
  function arrange(pick: string | null, from: DeckSlots = arranged, tabs: string[] = order) {
    const next = placeSlots(from, { order: tabs, layout: panes, pick, at: hi });
    const at = pick ? next.indexOf(pick) : -1;
    hi = at >= 0 ? at : Math.min(hi, panes - 1);

    if (next.length !== arranged.length || next.some((id, i) => id !== arranged[i])) {
      arranged = next;
      onslots?.(next);
    }
  }

  /**
   * A tab hidden or cleared off the strip gives up its pane, which the next
   * tab along fills. A session that ends or goes stays where it was; this
   * one was taken away on purpose.
   */
  function release(id: string, away?: (id: string) => void) {
    away?.(id);
    const freed = arranged.map((one) => (one === id ? null : one));
    // Left out of the fill, in case the strip has not let it go yet.
    arrange(freed[hi] ?? null, freed, order.filter((one) => one !== id));
  }

  // Sessions arriving fill empty panes, and a change of layout adds or drops
  // panes, keeping the highlighted session on the screen.
  $effect(() => {
    void order;
    void panes;
    untrack(() => arrange(arranged[hi] ?? null));
  });

  // A new session named by the page, #session/<id>, picks its tab; only a
  // change of that, not a pick made here, moves the deck.
  $effect(() => {
    const named = current;

    untrack(() => {
      if (named && order.includes(named)) {
        arrange(named);
      }
    });
  });

  $effect(() => {
    if (typeof matchMedia !== 'function') {
      return;
    }

    const query = matchMedia('(max-width: 899px)');
    const read = () => (narrow = query.matches);
    read();
    query.addEventListener?.('change', read);

    return () => query.removeEventListener?.('change', read);
  });

  function register(id: string) {
    return (handle: PaneHandle | null) => {
      if (handle) {
        handles.set(id, handle);
      } else {
        handles.delete(id);
      }
    };
  }

  export function pick(id: string, focus = true) {
    arrange(id);
    onpick?.(id);

    if (focus) {
      // A pane newly shown hands its handle back once it is on the page.
      queueMicrotask(() => setTimeout(() => handles.get(id)?.focus(), 0));
    }
  }

  function setLayout(next: DeckLayout) {
    morph(() => (panes = next), { noVT: true, target: host?.querySelector<HTMLElement>('.panes') });
    onlayout?.(next);
  }

  function keys(event: KeyboardEvent) {
    const to = deckKey(event, order, shown, on);

    if (to) {
      event.preventDefault();
      event.stopPropagation();
      pick(to);
    }
  }

  function find(event?: KeyboardEvent) {
    const backwards = !!event?.shiftKey;

    if (event && event.key !== 'Enter') {
      if (event.key === 'Escape') {
        term = '';
        handles.get(on ?? '')?.clearFind();
        found = '';
      }

      return;
    }

    event?.preventDefault();
    const hit = on ? handles.get(on)?.find(term.trim(), backwards) : false;
    found = term.trim() ? (hit ? 'found' : 'no match') : '';
  }
</script>

<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<section bind:this={host} class="ls-deck" aria-label="Terminals" onkeydowncapture={keys} onleaveterminal={() => host?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus()}>
  <div class="dtop">
    <SessionTabs
      {sessions}
      {hidden}
      {changes}
      current={on}
      controls={panesId}
      onpick={(id) => pick(id, false)}
      onmaximise={(id) => { pick(id); setLayout(1); }}
      {onnew}
      {onmove}
      {onrename}
      onhide={(id) => release(id, onhide)}
      {onunhide}
      onclear={(id) => release(id, onclear)}
      {onend}
    />
    <div class="lay" role="radiogroup" aria-label="Panes">
      {#each LAYOUTS as [count, name, icon] (count)}
        <button type="button" role="radio" aria-checked={count === panes ? 'true' : 'false'} aria-label={name} title={name} onclick={() => setLayout(count)}>
          <svg viewBox="0 0 20 16" aria-hidden="true"><path d={icon} fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" /></svg>
        </button>
      {/each}
    </div>
  </div>
  <div class="dtop">
    {#if waiting.length}
      <HaloButton label="Go to the one waiting" kind="needs" size="small" onpress={() => pick(waiting[0].id)} />
    {/if}
    <div class="find">
      <input
        class="ls-text"
        type="search"
        placeholder="Find in this terminal"
        aria-label="Find in the focused terminal: Enter for the next, Shift Enter for the one before"
        autocomplete="off"
        bind:value={term}
        oninput={() => find()}
        onkeydown={find}
      />
      <span role="status">{found}</span>
    </div>
    <span class="grow"></span>
    <span class="keys">Ctrl Alt 1–9 picks · Ctrl Alt arrows move · Ctrl ] leaves a terminal · F2 renames a tab · Ctrl Shift arrows move it</span>
  </div>
  <div class={`panes l${narrow ? 1 : panes}`} id={panesId}>
    {#each shown as id (id)}
      <div class="pane" class:on={id === on} data-id={id} onfocusin={() => { if (on !== id) { hi = arranged.indexOf(id); onpick?.(id); } }}>
        {#if listed && !order.includes(id)}
          <p class="gone" role="note">This session is no longer in Studio. The pane keeps it until you pick another tab.</p>
        {/if}
        {@render pane(id, register(id))}
      </div>
    {/each}
    {#if !shown.length}
      <p class="calm">No sessions are running. Start one with the + beside the tabs, or N.</p>
    {/if}
  </div>
</section>
