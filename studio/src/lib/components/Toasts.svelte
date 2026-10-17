<script lang="ts">
  // The notification stack, top right and away from the rail. Done, notices
  // and Needs you are read out politely; a Problem at once.
  import { onMount } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { LEVELS, toasts as shared, type ToastStack, type Toast } from '../toasts.svelte';
  import { fullMotion } from '../studio';

  let { stack = shared, clock = true }: { stack?: ToastStack; clock?: boolean } = $props();

  onMount(() => {
    if (!clock) {
      return;
    }

    const timer = setInterval(() => stack.tick(), 1000);

    return () => clearInterval(timer);
  });

  function level(toast: Toast) {
    return toast.settled !== null ? 'done' : toast.level;
  }

  async function answer(toast: Toast, action: { label: string; onpress?: () => string | void | Promise<string | void> }) {
    const words = await action.onpress?.();
    stack.settle(toast.id, words || action.label);
  }
</script>

<div
  class="ls-toasts"
  role="region"
  aria-label="Notifications"
  onmouseenter={() => (stack.hold = true)}
  onmouseleave={() => (stack.hold = false)}
  onfocusin={() => (stack.hold = true)}
  onfocusout={() => (stack.hold = false)}
>
  {#each stack.list as toast (toast.id)}
    {@const shown = level(toast)}
    {#if toast.folded}
      <button
        type="button"
        class={`ls-toast-chip ${shown}`}
        class:anim={toast.fresh && fullMotion()}
        aria-label={`${LEVELS[shown][1]}: ${toast.text}. Open it`}
        onclick={() => stack.open(toast.id)}
      ><span class="gl" aria-hidden="true">{LEVELS[shown][0]}</span><b>{LEVELS[shown][1]}</b><span>{toast.text}</span></button>
    {:else}
      <div class={`ls-toast ${shown}`} class:anim={toast.fresh && fullMotion()}>
        <div class="th">
          <span class="gl" aria-hidden="true">{LEVELS[shown][0]}</span>
          <span class="lv">{LEVELS[shown][1]}</span>
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
</div>
<p role="status" class="ls-sr">{stack.polite}</p>
<p role="alert" class="ls-sr">{stack.alert}</p>
