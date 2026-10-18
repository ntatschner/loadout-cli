<script lang="ts">
  // One of the page components' glyphs, hidden from assistive technology:
  // the word beside it is what is read.
  import { GLYPHS, type GlyphName } from '../glyphs';

  let { name, cls = undefined }: { name: GlyphName; cls?: string } = $props();
</script>

<svg viewBox="0 0 20 20" aria-hidden="true" class={cls}><path d={GLYPHS[name]} fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" /></svg>
