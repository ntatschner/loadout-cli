<script lang="ts">
  // The notifications. On most pages, a stack top right and away from the
  // rail. In a working view (a terminal or a split: the deck, a page beside
  // it or the board, a session's page, the board, Pure Focus) everything
  // arrives as a chip and nothing opens as a card by itself; with
  // studio-notices-working at strip or off they live in a lane of their own
  // under the mode bar, which takes its own height, so none of them covers a
  // pane. The lane counts each level, and a level's cards open below it only
  // when asked. Done, notices and Needs you are read out politely; a Problem
  // at once, whichever way they show. Nothing ever moves.
  import { onMount, untrack } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { LEVELS, shownLevel, toasts as shared, type NoticesWorking, type ToastLevel, type ToastPlace, type ToastStack, type Toast } from '../toasts.svelte';
  import { fullMotion } from '../studio';

  let {
    stack = shared,
    clock = true,
    place = null,
    notices = 'strip',
  }: { stack?: ToastStack; clock?: boolean; place?: ToastPlace; notices?: NoticesWorking } = $props();

  /** How close, in pixels, the pointer comes before a Done or notice chip gives way. */
  const NEAR = 48;
  /** The lane's badges, most pressing first. */
  const ORDER: ToastLevel[] = ['needs', 'problem', 'info', 'done'];

  let root = $state<HTMLDivElement>();
  let near = $state(false);
  let showHistory = $state(false);
  /** The level whose cards are open below the lane, which only a press opens. */
  let opened = $state<ToastLevel | null>(null);

  /** In a working view, unless the setting keeps the floating chips. */
  const lane = $derived(place === 'working' && notices !== 'chips');
  const counts = $derived(stack.counts);
  const newest = $derived(stack.list[0]);
  const dropped = $derived(opened ? stack.list.filter((one) => shownLevel(one) === opened) : []);

  onMount(() => {
    if (!clock) {
      return;
    }

    const timer = setInterval(() => stack.tick(), 1000);

    return () => clearInterval(timer);
  });

  $effect(() => {
    const tucked = place !== null;

    // Only the place is followed: tuck reads and writes the list.
    untrack(() => stack.tuck(tucked));
  });

  // The page makes room for the lane while it is there, the same height
  // whether anything is in it or not, so a toast never refits a terminal.
  $effect(() => {
    if (!lane) {
      return;
    }

    const html = document.documentElement;
    html.setAttribute('data-lane', '');

    return () => html.removeAttribute('data-lane');
  });

  // A level with nothing left in it has no cards to show.
  $effect(() => {
    if (opened && !counts[opened]) {
      opened = null;
    }
  });

  // A chip that lets clicks through never sees the pointer, so the page is
  // watched instead, and only while the floating chips are over a working view.
  $effect(() => {
    if (place === null || lane) {
      near = false;

      return;
    }

    function watch(event: PointerEvent) {
      const chips = root ? Array.from(root.querySelectorAll<HTMLElement>('[data-gives-way]')) : [];

      near = chips.some((chip) => {
        const box = chip.getBoundingClientRect();
        const dx = Math.max(box.left - event.clientX, 0, event.clientX - box.right);
        const dy = Math.max(box.top - event.clientY, 0, event.clientY - box.bottom);

        return Math.hypot(dx, dy) <= NEAR;
      });
    }

    window.addEventListener('pointermove', watch, { passive: true });

    return () => window.removeEventListener('pointermove', watch);
  });

  /** Done and notices give way over a working view; a question waiting on you never does. */
  function givesWay(toast: Toast) {
    return place !== null && !lane && shownLevel(toast) !== 'needs' && shownLevel(toast) !== 'problem';
  }

  async function answer(toast: Toast, action: { label: string; onpress?: () => string | void | Promise<string | void> }) {
    const words = await action.onpress?.();
    stack.settle(toast.id, words || action.label);
  }

  function toggle(level: ToastLevel) {
    showHistory = false;
    opened = opened === level ? null : level;
  }

  // Ctrl+Shift+X, caught before a terminal takes it: folds what is open, and
  // pressed again clears what is not waiting on you. In the lane, what is
  // open is the cards below it.
  function sweepKey(event: KeyboardEvent) {
    if (!(event.ctrlKey && event.shiftKey && !event.altKey && !event.metaKey && event.code === 'KeyX')) {
      return;
    }

    if (lane && (opened || showHistory)) {
      event.preventDefault();
      event.stopPropagation();
      opened = null;
      showHistory = false;
    } else if (stack.list.length) {
      event.preventDefault();
      event.stopPropagation();
      stack.sweep();
    }
  }

  function closeOnEscape(event: KeyboardEvent) {
    if (event.key === 'Escape' && (opened || showHistory)) {
      event.preventDefault();
      event.stopPropagation();
      const was = opened;
      opened = null;
      showHistory = false;
      root?.querySelector<HTMLElement>(was ? `.ls-lane-badge.${was}` : '.ls-toast-past')?.focus();
    }
  }

  function pointedAt(id: number) {
    stack.pointed = id;
  }

  function leftFrom(id: number) {
    if (stack.pointed === id) {
      stack.pointed = null;
    }
  }

  function unfocused(id: number) {
    if (stack.focused === id) {
      stack.focused = null;
    }
  }
</script>

<svelte:window onkeydowncapture={sweepKey} />

{#snippet chip(toast: Toast, onpress: () => void)}
  {@const shown = shownLevel(toast)}
  <button
    type="button"
    class={`ls-toast-chip ${shown}`}
    class:anim={toast.fresh && fullMotion() && !lane}
    data-gives-way={givesWay(toast) ? '' : undefined}
    aria-label={`${LEVELS[shown][1]}: ${toast.text}${toast.count > 1 ? `, ${toast.count} times` : ''}. Open it`}
    onclick={onpress}
    onmouseenter={() => pointedAt(toast.id)}
    onmouseleave={() => leftFrom(toast.id)}
    onfocusin={() => (stack.focused = toast.id)}
    onfocusout={() => unfocused(toast.id)}
  ><span class="gl" aria-hidden="true">{LEVELS[shown][0]}</span><b>{LEVELS[shown][1]}</b><span>{toast.text}</span>{#if toast.count > 1}<i class="n" aria-hidden="true">×{toast.count}</i>{/if}</button>
{/snippet}

{#snippet card(toast: Toast)}
  {@const shown = shownLevel(toast)}
  <div
    class={`ls-toast ${shown}`}
    class:anim={toast.fresh && fullMotion() && !lane}
    role="group"
    aria-label={LEVELS[shown][1]}
    onmouseenter={() => pointedAt(toast.id)}
    onmouseleave={() => leftFrom(toast.id)}
    onfocusin={() => (stack.focused = toast.id)}
    onfocusout={() => unfocused(toast.id)}
  >
    <div class="th">
      <span class="gl" aria-hidden="true">{LEVELS[shown][0]}</span>
      <span class="lv">{LEVELS[shown][1]}{#if toast.count > 1}<i class="n"> ×{toast.count}</i>{/if}</span>
      <button type="button" class="x" aria-label={`Dismiss: ${toast.text}`} onclick={() => stack.dismiss(toast.id)}>✕</button>
    </div>
    <p>{toast.text}</p>
    {#if toast.cmd}<code class="ls-cmd">{toast.cmd}</code>{/if}
    {#if toast.actions.length}
      <div class="acts">
        {#each toast.actions as action (action.label)}
          <HaloButton label={action.label} kind={action.kind ?? ''} size="small" cmd={action.cmd} onpress={() => answer(toast, action)} />
        {/each}
      </div>
    {/if}
  </div>
{/snippet}

{#snippet history()}
  {#if stack.history.length}
    <button type="button" class="ls-toast-past" aria-expanded={showHistory} onclick={() => { opened = null; showHistory = !showHistory; }}>
      Earlier <b>{stack.history.length}</b>
    </button>
    {#if showHistory}
      <div class="ls-toast-history" role="group" aria-label="Earlier notifications, this session">
        <ul>
          {#each stack.history as gone (gone.id)}
            <li class={gone.level}>
              <span class="gl" aria-hidden="true">{LEVELS[gone.level][0]}</span>
              <span class="t"><b>{LEVELS[gone.level][1]}</b> {gone.text}{#if gone.count > 1}<i class="n"> ×{gone.count}</i>{/if}</span>
              {#if gone.cmd}<code class="ls-cmd">{gone.cmd}</code>{/if}
            </li>
          {/each}
        </ul>
        <button type="button" class="clear" onclick={() => { stack.clearHistory(); showHistory = false; }}>Clear</button>
      </div>
    {/if}
  {/if}
{/snippet}

{#if lane}
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div bind:this={root} class="ls-lane" class:calm={!fullMotion()} data-notices={notices} role="region" aria-label="Notifications" aria-keyshortcuts="Control+Shift+X" onkeydown={closeOnEscape}>
    <div class="badges">
      {#each ORDER as level (level)}
        {#if counts[level]}
          <button
            type="button"
            class={`ls-lane-badge ${level}`}
            class:anim={newest?.fresh && shownLevel(newest) === level && fullMotion()}
            aria-expanded={opened === level}
            aria-label={`${LEVELS[level][1]}: ${counts[level]}. ${opened === level ? 'Hide them' : 'Show them'}`}
            onclick={() => toggle(level)}
          ><span class="gl" aria-hidden="true">{LEVELS[level][0]}</span><b aria-hidden="true">{counts[level]}</b></button>
        {/if}
      {/each}
      {#if !stack.list.length}
        <span class="none">No notifications</span>
      {/if}
    </div>
    {#if notices === 'strip' && newest}
      <div class="newest">{@render chip(newest, () => toggle(shownLevel(newest)))}</div>
    {/if}
    <span class="grow"></span>
    {@render history()}
    {#if dropped.length}
      <div class="ls-lane-drop" role="group" aria-label={`${LEVELS[opened ?? 'info'][1]}, ${dropped.length}`}>
        {#each dropped as toast (toast.id)}
          {@render card(toast)}
        {/each}
      </div>
    {/if}
  </div>
{:else}
  <div bind:this={root} class="ls-toasts" class:near class:calm={!fullMotion()} role="region" aria-label="Notifications" aria-keyshortcuts="Control+Shift+X">
    {#each stack.list as toast (toast.id)}
      {#if toast.folded}
        {@render chip(toast, () => stack.open(toast.id))}
      {:else}
        {@render card(toast)}
      {/if}
    {/each}
    {@render history()}
  </div>
{/if}
<p role="status" class="ls-sr">{stack.polite}</p>
<p role="alert" class="ls-sr">{stack.alert}</p>
