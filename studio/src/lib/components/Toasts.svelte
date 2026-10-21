<script lang="ts">
  // The notification stack, top right and away from the rail. Done, notices
  // and Needs you are read out politely; a Problem at once. In a named place
  // everything shows as a chip, and Done and notice chips go see-through and
  // let clicks pass while the pointer is near. Nothing ever moves.
  import { onMount, untrack } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { LEVELS, shownLevel, toasts as shared, type ToastPlace, type ToastStack, type Toast } from '../toasts.svelte';
  import { fullMotion } from '../studio';

  let { stack = shared, clock = true, place = null }: { stack?: ToastStack; clock?: boolean; place?: ToastPlace } = $props();

  /** How close, in pixels, the pointer comes before a Done or notice chip gives way. */
  const NEAR = 48;

  let root = $state<HTMLDivElement>();
  let near = $state(false);
  let showHistory = $state(false);

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

  // A chip that lets clicks through never sees the pointer, so the page is
  // watched instead, and only while in a named place.
  $effect(() => {
    if (place === null) {
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

  /** Done and notices give way in a named place; a question waiting on you never does. */
  function givesWay(toast: Toast) {
    return place !== null && shownLevel(toast) !== 'needs' && shownLevel(toast) !== 'problem';
  }

  async function answer(toast: Toast, action: { label: string; onpress?: () => string | void | Promise<string | void> }) {
    const words = await action.onpress?.();
    stack.settle(toast.id, words || action.label);
  }

  // Ctrl+Shift+X, caught before a terminal takes it: folds what is open, and
  // pressed again clears what is not waiting on you.
  function sweepKey(event: KeyboardEvent) {
    if (event.ctrlKey && event.shiftKey && !event.altKey && !event.metaKey && event.code === 'KeyX' && stack.list.length) {
      event.preventDefault();
      event.stopPropagation();
      stack.sweep();
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

<div bind:this={root} class="ls-toasts" class:near class:calm={!fullMotion()} role="region" aria-label="Notifications" aria-keyshortcuts="Control+Shift+X">
  {#each stack.list as toast (toast.id)}
    {@const shown = shownLevel(toast)}
    {#if toast.folded}
      <button
        type="button"
        class={`ls-toast-chip ${shown}`}
        class:anim={toast.fresh && fullMotion()}
        data-gives-way={givesWay(toast) ? '' : undefined}
        aria-label={`${LEVELS[shown][1]}: ${toast.text}${toast.count > 1 ? `, ${toast.count} times` : ''}. Open it`}
        onclick={() => stack.open(toast.id)}
        onmouseenter={() => pointedAt(toast.id)}
        onmouseleave={() => leftFrom(toast.id)}
        onfocusin={() => (stack.focused = toast.id)}
        onfocusout={() => unfocused(toast.id)}
      ><span class="gl" aria-hidden="true">{LEVELS[shown][0]}</span><b>{LEVELS[shown][1]}</b><span>{toast.text}</span>{#if toast.count > 1}<i class="n" aria-hidden="true">×{toast.count}</i>{/if}</button>
    {:else}
      <div
        class={`ls-toast ${shown}`}
        class:anim={toast.fresh && fullMotion()}
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
    {/if}
  {/each}

  {#if stack.history.length}
    <button type="button" class="ls-toast-past" aria-expanded={showHistory} onclick={() => (showHistory = !showHistory)}>
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
</div>
<p role="status" class="ls-sr">{stack.polite}</p>
<p role="alert" class="ls-sr">{stack.alert}</p>
