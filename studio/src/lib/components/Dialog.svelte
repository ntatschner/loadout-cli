<script lang="ts">
  // Studio's modal: a projected panel that opens as a line of light and
  // unfolds over a dim, while the page behind steps back. Focus moves in, is
  // held there, and returns to what opened it; Escape and the dim close it.
  import type { Snippet } from 'svelte';
  import { onMount, tick } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import type { HaloButtonOptions } from '../types';
  import { focusables, trapTab } from '../focus';
  import { fullMotion } from '../studio';
  import { portal } from '../portal';

  interface Props {
    title?: string;
    label?: string;
    why?: string;
    danger?: boolean;
    role?: 'dialog' | 'alertdialog';
    /** HaloButtons; each closes the dialog unless its onpress returns true. A Cancel follows them. */
    actions?: (HaloButtonOptions & { onpress?: () => unknown })[];
    cancelLabel?: string;
    /** A CSS selector inside the panel to focus first. */
    initialFocus?: string;
    /** An id for the page behind, which steps back while this is open. */
    behind?: string;
    extraClass?: string;
    onclose?: () => void;
    children?: Snippet;
  }

  let {
    title = undefined,
    label = undefined,
    why = undefined,
    danger = false,
    role = danger ? 'alertdialog' : 'dialog',
    actions = [],
    cancelLabel = 'Cancel',
    initialFocus = undefined,
    behind = 'main',
    extraClass = '',
    onclose = undefined,
    children = undefined,
  }: Props = $props();

  const id = `ls-dlg-${Math.random().toString(36).slice(2, 7)}`;
  let panel = $state<HTMLDivElement | undefined>();
  let opener: HTMLElement | null = null;
  const projected = fullMotion();

  onMount(() => {
    opener = document.activeElement as HTMLElement | null;
    const page = behind ? document.getElementById(behind) : null;
    page?.classList.add('ls-behind', 'is-back');

    // Everything outside the dialog is out of reach while it is open, for a
    // screen reader as well as for Tab; aria-modal alone is not honoured
    // everywhere.
    const outside = Array.from(document.querySelectorAll<HTMLElement>('#main, .ls-nav, .skip')).filter((el) => !el.inert);
    outside.forEach((el) => (el.inert = true));

    tick().then(() => {
      if (!panel) {
        return;
      }

      const list = focusables(panel);
      const first = initialFocus ? panel.querySelector<HTMLElement>(initialFocus) : danger ? list[list.length - 1] : list[0];
      first?.focus();
    });

    return () => {
      page?.classList.remove('is-back');
      outside.forEach((el) => (el.inert = false));
      opener?.focus?.();
    };
  });

  function close() {
    onclose?.();
  }

  function keys(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close();

      return;
    }

    if (panel) {
      trapTab(event, panel);
    }
  }

  async function act(action: { onpress?: () => unknown }) {
    const keep = await action.onpress?.();

    if (keep !== true) {
      close();
    }
  }
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div class="ls-scrim" use:portal onclick={close}></div>
<div
  bind:this={panel}
  use:portal
  class={`ls-dialog ${danger ? 'danger' : ''} ${projected ? 'anim' : 'fade'} ${extraClass}`}
  {role}
  aria-modal="true"
  aria-labelledby={title ? id : undefined}
  aria-label={title ? undefined : label}
  tabindex="-1"
  onkeydown={keys}
>
  {#if title}<h2 {id}>{title}</h2>{/if}
  {#if why}<p class="why">{why}</p>{/if}
  {@render children?.()}
  {#if actions.length}
    <div class="acts">
      {#each actions as action (action.label)}
        <HaloButton {...action} onpress={() => act(action)} />
      {/each}
      <HaloButton label={cancelLabel} size="small" onpress={close} />
    </div>
  {/if}
</div>
