<script lang="ts">
  // What just happened to an item, with its age. It stays until the item is
  // looked at; the ping that drew the eye to it is ping(), below.
  import { MARK_GLYPH, type SignalFamily } from '../signals';

  let { family = 'moving', text, age = 'just now' }: { family?: SignalFamily; text: string; age?: string } = $props();
</script>

<span class={`ls-mark ${family}`}><i aria-hidden="true">{MARK_GLYPH[family]}</i>{text}<span class="age">{age}</span></span>
