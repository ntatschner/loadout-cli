<script lang="ts">
  // The Map's status band: how many are at each station, the oldest question
  // waiting, tokens today, and what is disrupted.
  import { css } from '../css';
  import { STAGE_NAMES } from '../signals';
  import { STAGE_INK, lineColour, stageOf, type TubeLine } from '../tube';

  let { lines, clock = '', oldest = undefined, tokens = undefined, label = 'Network status' }: { lines: TubeLine[]; clock?: string; oldest?: string; tokens?: string; label?: string } = $props();

  const GLYPH = ['○', '◇', '▶', '!', '✓'];
  const disrupted = $derived(lines.filter((line) => line.blocked || line.failed));
</script>

<section class="ls-status" aria-label={label}>
  <h2>Network status</h2>
  <div class="chips" role="list" aria-label="How many are at each station">
    {#each STAGE_NAMES as name, k (k)}
      <span role="listitem" use:css={{ color: STAGE_INK[k] }}>{GLYPH[k]} {name}<b>{lines.filter((line) => stageOf(line) === k).length}</b></span>
    {/each}
  </div>
  <span class="clock">{clock}</span>
  <div class="chips">
    <span>Oldest question waiting<b>{oldest || 'none'}</b></span>
    {#if tokens}<span>Tokens today<b>{tokens}</b></span>{/if}
  </div>
  <span></span>
  <ul class="disrupt" aria-label="Disruptions">
    {#each disrupted as line (line.id)}
      <li><span class="ls-badge" aria-hidden="true" use:css={{ '--lc': lineColour(line.code) }}>{line.code}</span><span>{line.title} · {line.blocked ? `blocked: ${line.blocked}` : line.failed}</span></li>
    {/each}
    <li><span class="ok" aria-hidden="true">✓</span><span class="ok left">{disrupted.length ? 'Good service on every other line' : 'Good service on every line'}</span></li>
  </ul>
</section>
