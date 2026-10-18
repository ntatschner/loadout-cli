<script lang="ts">
  // One of the six pages opened beside what you are watching, the terminals
  // or the runs board, which stay live. Close or Escape closes it, but not
  // while you are typing in a field, inside a terminal (Escape belongs to the
  // agent there) or in a dialog.
  import { onMount, type Snippet } from 'svelte';
  import { typingIn } from '../pure';

  interface Props {
    page: string;
    /** What it is beside: "your terminals", "the runs board". */
    beside: string;
    onclose: () => void;
    children: Snippet;
  }

  let { page, beside, onclose, children }: Props = $props();

  function onKey(event: KeyboardEvent) {
    if (event.key !== 'Escape' || event.defaultPrevented || typingIn(event.target) || document.querySelector('.ls-dialog')) {
      return;
    }

    event.preventDefault();
    onclose();
  }

  onMount(() => {
    document.addEventListener('keydown', onKey);

    return () => document.removeEventListener('keydown', onKey);
  });
</script>

<aside class="ls-beside" aria-label={`${page}, beside ${beside}`}>
  <div class="bh">
    <span class="t"><b>{page}</b> · beside {beside}</span>
    <span class="grow"></span>
    <button type="button" class="x" aria-keyshortcuts="Escape" onclick={onclose}>Close<kbd aria-hidden="true">Esc</kbd></button>
  </div>
  <div class="body">{@render children()}</div>
</aside>
