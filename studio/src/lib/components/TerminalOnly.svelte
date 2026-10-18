<script lang="ts">
  // What Studio never does from a page: the command, why it is done in a
  // terminal, and Copy, which copies the line and runs nothing. Never a
  // button that does it. A fact beside it may say how things stand, such as
  // whether a passphrase is set, but never a value.
  import Glyph from './Glyph.svelte';
  import HaloButton from './HaloButton.svelte';

  interface Props {
    command: string;
    title?: string;
    /** Completes "It …": the reason from Studio's coverage list, word for word. */
    why?: string;
    fact?: string;
  }

  let { command, title = 'Done in a terminal', why = undefined, fact = undefined }: Props = $props();

  let said = $state('');

  async function copy() {
    try {
      await navigator.clipboard.writeText(command.replace(/^\$ /, ''));
      said = '✓ Copied. Paste it in a terminal.';
    } catch {
      said = 'Select the line to copy it.';
    }
  }
</script>

<div class="ls-tonly">
  <div class="top"><b>{title}</b> <span class="ls-tag"><Glyph name="lock" />terminal only</span></div>
  {#if why}<p class="why">{why}</p>{/if}
  <div class="line"><code>{command}</code><HaloButton label="Copy" size="small" cmd="copies the line · runs nothing" onpress={() => void copy()} /></div>
  {#if fact}<p class="fact">{fact}</p>{/if}
  <p class="ls-said" role="status">{said}</p>
</div>
