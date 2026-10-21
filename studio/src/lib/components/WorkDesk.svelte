<script lang="ts">
  // Pure Focus: the list, the view and the actions in three panes over a
  // command bar, every key printed beside what it does. Single keys work
  // only while you are not typing: in a terminal every key goes to the
  // agent, and Ctrl+] leaves it. With Studio's single-letter shortcuts off,
  // only Escape and the list's own arrows, Home, End and Enter work; F6,
  // the buttons and the command bar still do. The keys are bound while
  // this is on the page and gone when it is not. The dividers between the
  // panes set how wide each is; below 1024px the panes stack and they go.
  import { onMount, tick, type Snippet } from 'svelte';
  import ActionPane from './ActionPane.svelte';
  import CommandBar from './CommandBar.svelte';
  import KeyHelp from './KeyHelp.svelte';
  import Signal from './Signal.svelte';
  import Splitter from './Splitter.svelte';
  import { signalOf } from '../signals';
  import { clampSplit, floorOf, terminalFloor } from '../splits';
  import { filtered, GO_KEYS, PAGE_KEYS, typingIn, type PureAction, type PureCommand, type PureGroup, type PureItem, type PureScope } from '../pure';

  interface Props {
    groups: PureGroup[];
    selected?: string | null;
    /** Studio's single-letter shortcuts. */
    keys?: boolean;
    label?: string;
    /** What the view shows for the picked thing. Made once per thing, so a change of its state does not remake it. */
    view: Snippet<[PureItem]>;
    actions: (item: PureItem) => PureAction[];
    commands: (typed: string, item: PureItem | null) => PureCommand[];
    ongo?: (scope: PureScope) => void;
    /** G then I, P, C, M, U or O: a page, as its own desk. */
    onpage?: (page: string) => void;
    onselect?: (id: string) => void;
    /** How much of the width the list and the actions were left with, kept by the page; null for the default. */
    split?: number[] | null;
    onsplit?: (fractions: number[]) => void;
  }

  let { groups, selected = null, keys = true, label = 'Everything live', view, actions, commands, ongo = undefined, onpage = undefined, onselect = undefined, split = null, onsplit = undefined }: Props = $props();

  const listId = `ls-wd-${Math.random().toString(36).slice(2, 7)}`;
  // svelte-ignore state_referenced_locally
  let picked = $state<string | null>(selected);
  let viewing = $state(false);
  let filter = $state('');
  let help = $state(false);
  let desk = $state<HTMLElement | undefined>();
  let list = $state<HTMLElement | undefined>();
  let filterBox = $state<HTMLInputElement | undefined>();
  let body = $state<HTMLElement | undefined>();
  let actPane = $state<HTMLElement | undefined>();
  let pane = $state<ReturnType<typeof ActionPane> | undefined>();
  let bar = $state<ReturnType<typeof CommandBar> | undefined>();
  let going = false;
  let goTimer: ReturnType<typeof setTimeout> | null = null;

  // The list and the actions keep the floors they always had; the view, which
  // may hold a terminal, a terminal's. Until a divider is moved the panes
  // are as the stylesheet draws them, and the dividers say where that is;
  // putting one back gives its pane the width the stylesheet would.
  let wide = $state(0);
  let listWide = $state(0);
  let actsWide = $state(0);
  const floor = terminalFloor().width;
  const listMin = $derived(floorOf(250, wide));
  const actsMin = $derived(floorOf(230, wide));
  const viewMin = $derived(floorOf(floor, wide));
  const listAt = $derived(split?.[0] ?? (wide > 0 ? listWide / wide : 0.25));
  const actsAt = $derived(split?.[1] ?? (wide > 0 ? actsWide / wide : 0.2));
  const columns = $derived(split ? `minmax(250px, ${listAt.toFixed(4)}fr) minmax(0, ${(1 - listAt - actsAt).toFixed(4)}fr) minmax(230px, ${actsAt.toFixed(4)}fr)` : undefined);

  function setList(to: number) {
    onsplit?.([to, actsAt]);
  }

  function setActs(boundary: number) {
    onsplit?.([listAt, 1 - boundary]);
  }

  const shownGroups = $derived(filtered(groups, filter));
  const visible = $derived(shownGroups.flatMap((group) => group.items));
  const everything = $derived(groups.flatMap((group) => group.items));
  // What is picked: kept while it is in the list, the first otherwise.
  const current = $derived(everything.find((item) => item.id === picked) ?? visible[0] ?? null);
  const itemActions = $derived(current ? actions(current) : []);
  const optionId = (item: PureItem) => `${listId}-${item.id.replace(/[^A-Za-z0-9_-]/g, '_')}`;

  $effect(() => {
    if (selected && selected !== picked && everything.some((item) => item.id === selected)) {
      picked = selected;
    }
  });

  export async function select(id: string, quiet = false) {
    picked = id;
    onselect?.(id);
    await tick();
    document.getElementById(optionId(current!))?.scrollIntoView?.({ block: 'nearest' });

    if (!quiet) {
      list?.focus({ preventScroll: true });
    }
  }

  function step(by: number) {
    if (!visible.length) {
      return;
    }

    const at = visible.findIndex((item) => item.id === current?.id);
    const next = by === Infinity ? visible.length - 1 : by === -Infinity ? 0 : Math.max(0, Math.min(visible.length - 1, (at < 0 ? 0 : at) + by));
    void select(visible[next].id);
  }

  export async function openView() {
    viewing = true;
    await tick();
    const terminal = body?.querySelector<HTMLElement>('.xterm-helper-textarea');
    const first = body?.querySelector<HTMLElement>('button, [href], input, textarea, [tabindex="0"]');
    (terminal ?? first ?? body)?.focus();
  }

  export function closeView() {
    viewing = false;
    list?.focus({ preventScroll: true });
  }

  function panes(): (HTMLElement | undefined)[] {
    return [list, body, actPane?.querySelector<HTMLElement>('button') ?? actPane, desk?.querySelector<HTMLElement>('.ls-cmdbar input') ?? undefined];
  }

  function nextPane(by: number) {
    const all = panes();
    const now = document.activeElement;
    const at = all.findIndex((one) => one && (one === now || one.contains(now)));
    const next = all[(at + by + all.length) % all.length];

    if (next === body) {
      void openView();
    } else {
      next?.focus();
    }
  }

  function onKey(event: KeyboardEvent) {
    if (!desk || help) {
      return;
    }

    const target = event.target as HTMLElement | null;

    if (event.key === 'F6') {
      event.preventDefault();
      nextPane(event.shiftKey ? -1 : 1);

      return;
    }

    if (typingIn(target) || event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }

    const key = event.key;
    const onList = target === list;

    // Single-letter shortcuts off: only Escape and the list's own arrows, Home, End and Enter.
    if (!keys && !(key === 'Escape' || (onList && /^(ArrowUp|ArrowDown|Home|End|Enter)$/.test(key)))) {
      return;
    }

    if (going) {
      going = false;

      if (goTimer) {
        clearTimeout(goTimer);
      }

      const where = GO_KEYS[key.toLowerCase()];
      const page = PAGE_KEYS[key.toLowerCase()];

      if (where) {
        event.preventDefault();
        ongo?.(where);
      } else if (page) {
        event.preventDefault();
        onpage?.(page);
      }

      return;
    }

    if (key === 'Escape') {
      if (pane?.isArmed()) {
        event.preventDefault();
        pane.disarm();
      } else if (viewing || (desk.contains(target) && !onList)) {
        event.preventDefault();
        closeView();
      }

      return;
    }

    if (key === 'j' || key === 'J' || (key === 'ArrowDown' && onList)) {
      event.preventDefault();
      step(1);
    } else if (key === 'k' || key === 'K' || (key === 'ArrowUp' && onList)) {
      event.preventDefault();
      step(-1);
    } else if (onList && key === 'Home') {
      event.preventDefault();
      step(-Infinity);
    } else if (onList && key === 'End') {
      event.preventDefault();
      step(Infinity);
    } else if (onList && key === 'Enter') {
      event.preventDefault();
      void openView();
    } else if (key === '/') {
      event.preventDefault();
      filterBox?.focus();
      filterBox?.select();
    } else if (key === ':') {
      event.preventDefault();
      bar?.focus();
    } else if (key === '?') {
      event.preventDefault();
      help = true;
    } else if (key === 'g' || key === 'G') {
      event.preventDefault();
      going = true;
      goTimer = setTimeout(() => (going = false), 1500);
    } else if (/^[1-9]$/.test(key) && pane) {
      event.preventDefault();
      void pane.run(Number(key) - 1);
    }
  }

  function filterKeys(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      filter = '';
      list?.focus();
    } else if (event.key === 'Enter' || event.key === 'ArrowDown') {
      event.preventDefault();
      list?.focus();
    }
  }

  onMount(() => {
    document.addEventListener('keydown', onKey);

    return () => {
      document.removeEventListener('keydown', onKey);

      if (goTimer) {
        clearTimeout(goTimer);
      }
    };
  });
</script>

<div bind:this={desk} class="ls-desk" class:viewing bind:clientWidth={wide} style:--desk-cols={columns} role="application" aria-roledescription="Pure Focus" aria-label="Pure Focus" onleaveterminal={closeView}>
  <section class="list" aria-label="List" bind:offsetWidth={listWide}>
    <div class="ph"><span>List</span><span class="grow"></span><span class="hint" aria-hidden="true"><kbd class="ls-kbd">J</kbd><kbd class="ls-kbd">K</kbd>move</span></div>
    <div class="lwrap">
      <div class="filter">
        <kbd class="ls-kbd">/</kbd>
        <input bind:this={filterBox} bind:value={filter} type="search" placeholder="Filter" aria-label="Filter the list" autocomplete="off" aria-keyshortcuts="/" onkeydown={filterKeys} />
      </div>
      <ul bind:this={list} role="listbox" class="items" id={listId} tabindex="0" aria-label={label} aria-activedescendant={current ? optionId(current) : undefined}>
        {#each shownGroups as group (group.key)}
          <li role="presentation" class={`gh ${group.key === 'needs' ? 'needs' : ''}`}><span>{group.name}</span><span>{group.items.length}</span></li>
          {#each group.items as item (item.id)}
            <!-- The keys are the listbox's, which says which option is active: an option is only clicked. -->
            <!-- svelte-ignore a11y_click_events_have_key_events -->
            <li
              role="option"
              id={optionId(item)}
              data-id={item.id}
              class={item.word ? signalOf(item.word).family : 'plain'}
              aria-selected={item.id === current?.id ? 'true' : 'false'}
              onclick={() => void select(item.id)}
              ondblclick={() => {
                void select(item.id);
                void openView();
              }}
            >
              {#if item.word}<Signal word={item.word} />{:else}<span class="nosig" aria-hidden="true"></span>{/if}<span class="t">{item.title}</span><span class="x">{item.meta}</span>{#if item.word}<span class="w">{item.word}</span>{/if}
            </li>
          {/each}
        {:else}
          <li role="presentation" class="none">{filter ? `Nothing matches “${filter}”.` : 'Nothing is live.'}</li>
        {/each}
      </ul>
    </div>
  </section>

  <section class="view" aria-label="View">
    <div class="ph">
      {#if current}
        <button type="button" class="ls-halo small back" onclick={closeView}><span class="ring" aria-hidden="true"></span>List</button>
        <span class="vtitle"><b>{current.title}</b><span>{current.meta}</span></span>
        {#if current.word}<Signal word={current.word} />{/if}
        <span class="hint" aria-hidden="true"><kbd class="ls-kbd">Esc</kbd>list</span>
      {:else}
        <span>View</span>
      {/if}
    </div>
    <div bind:this={body} class="vbody" class:term={current?.kind === 'session'} tabindex="-1">
      {#if current}
        {#key current.id}
          {@render view(current)}
        {/key}
      {:else}
        <p class="none">Pick something in the list.</p>
      {/if}
    </div>
  </section>

  <section bind:this={actPane} class="acts" aria-label="Actions" bind:offsetWidth={actsWide}>
    <div class="ph"><span>Actions</span><span class="grow"></span><span class="hint" aria-hidden="true"><kbd class="ls-kbd">1</kbd>–<kbd class="ls-kbd">9</kbd></span></div>
    <div class="apwrap">
      <ActionPane bind:this={pane} actions={itemActions} owner={current?.id ?? null} emptyText={current ? 'Nothing to do with this one.' : 'Pick something in the list.'} />
    </div>
  </section>

  <div class="cbar">
    <CommandBar bind:this={bar} source={(typed) => commands(typed, current)} ondone={() => list?.focus()} />
  </div>

  <!-- After the panes, so the panes' own order is untouched. -->
  <Splitter class="first" label="Width of the list" value={clampSplit(listAt, listMin, 1 - actsAt - viewMin)} min={listMin} max={1 - actsAt - viewMin} reset={floorOf(320, wide)} onchange={setList} />
  <Splitter class="second" label="Width of the actions" value={clampSplit(1 - actsAt, listAt + viewMin, 1 - actsMin)} min={listAt + viewMin} max={1 - actsMin} reset={1 - floorOf(280, wide)} onchange={setActs} />
</div>

{#if help}
  <KeyHelp onclose={() => {
    help = false;
    list?.focus();
  }} />
{/if}
