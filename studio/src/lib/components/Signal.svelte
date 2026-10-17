<script lang="ts">
  // A state as four things at once: the word, a glyph in a ring, the family's
  // colour and its motion. The ring is aria-hidden; the word is the text.
  import { signalOf } from '../signals';

  let { word, big = false }: { word: string; big?: boolean } = $props();

  const signal = $derived(signalOf(word));
  const classes = $derived(['ls-sig', signal.family, `m-${signal.motion}`, big ? 'big' : ''].filter(Boolean).join(' '));
</script>

<span class={classes}><span class="ic" aria-hidden="true"><svg viewBox="0 0 20 20"><path d={signal.glyph} fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" /></svg></span><b>{word}</b></span>
