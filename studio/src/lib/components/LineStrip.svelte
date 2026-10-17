<script lang="ts">
  // The picked line drawn straight: what it is, where it is, its last events,
  // its checks and what you can do.
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import { css, glowOf } from '../css';
  import { STAGE_NAMES, wordFor } from '../signals';
  import { EVENT_GLYPH, lineColour, stageOf, type TubeEvent, type TubeLine } from '../tube';
  import type { HaloButtonOptions } from '../types';

  interface Props {
    line: TubeLine;
    facts?: { k: string; v: string }[];
    checks?: [string, string][];
    events?: TubeEvent[];
    actions?: (HaloButtonOptions & { onpress?: () => void })[];
    stations?: readonly string[];
    idleAfter?: number;
  }

  let { line, facts = [], checks = [], events = [], actions = [], stations = STAGE_NAMES, idleAfter = 10 }: Props = $props();

  const SX = [10, 125, 240, 355, 470];
  const colour = $derived(lineColour(line.code));
  const stage = $derived(stageOf(line));
  const tx = $derived(SX[stage] + 20);
  const word = $derived(wordFor(line, idleAfter));
  const pct = (x: number) => `${(x / 5.6).toFixed(2)}%`;
  const shown = $derived(events.slice(0, 5));
</script>

<section class="ls-strip" aria-label={`Line ${line.code}: ${line.title}`} use:css={{ '--lc': colour }}>
  <div class="who">
    <span class="ls-badge big" use:css={{ '--lc': colour }}>{line.code}</span>
    <h2>{line.title}</h2>
    <span class="where">{line.kind ?? ''}{line.where ? ` · ${line.where}` : ''}</span>
    <span class="sigline"><Signal {word} /></span>
    {#if line.note}<span class="note">{line.note}</span>{/if}
  </div>
  <div class="line">
    <div class="draw">
      <svg viewBox="0 0 560 120" aria-hidden="true">
        <line x1="20" y1="70" x2="540" y2="70" stroke-width="10" stroke-linecap="round" use:css={{ stroke: colour, opacity: 0.55, 'stroke-dasharray': '14 9' }} />
        <line x1="20" y1="70" x2={tx} y2="70" stroke-width="10" stroke-linecap="round" class="trk" use:css={{ stroke: colour, filter: glowOf(colour, 6) }} />
        {#each SX as x, k (k)}
          <line x1={x + 20} y1="58" x2={x + 20} y2="82" stroke-width="3" use:css={{ stroke: 'var(--ink-strong)', opacity: k <= stage ? null : 0.45 }} />
        {/each}
        <circle cx={tx} cy="70" r="13" stroke-width="3" use:css={{ fill: colour, stroke: 'var(--ground)', filter: glowOf(colour, 10) }} />
      </svg>
      {#each shown as event, j (j)}
        {@const same = shown.slice(0, j).filter((other) => other.stage === event.stage).length}
        <span
          class={`mk${event.kind === 'failed' || event.kind === 'blocked' ? ' bad' : event.kind === 'asked' ? ' needs' : ''}`}
          aria-hidden="true"
          use:css={{ '--lc': colour, left: pct(SX[event.stage] + 20 - 18 - same * 24), top: '25%' }}
        >{EVENT_GLYPH[event.kind] ?? '·'}</span>
      {/each}
      <span class="code" aria-hidden="true" use:css={{ left: pct(tx), top: '58.3%' }}>{line.code}</span>
    </div>
    <div class="names" aria-hidden="true">
      {#each SX as x, k (k)}
        <span class={k === stage ? 'cur' : ''} use:css={{ left: pct(x + 20) }}>{stations[k]}</span>
      {/each}
    </div>
    {#if shown.length}
      <ul class="events" aria-label="Last events">
        {#each shown as event, j (j)}
          <li><b>{event.t ?? ''}</b>{EVENT_GLYPH[event.kind] ?? '·'} {event.text}</li>
        {/each}
      </ul>
    {/if}
  </div>
  <div class="side">
    {#if facts.length}
      <dl>
        {#each facts as fact (fact.k)}<dt>{fact.k}</dt><dd>{fact.v}</dd>{/each}
      </dl>
    {/if}
    {#if checks.length}
      <ul class="chk" aria-label="Checks">
        {#each checks as check, j (j)}
          <li class={check[1]}><i aria-hidden="true">{check[1] === 'met' ? '✓' : check[1] === 'unmet' ? '✕' : '○'}</i>{check[0]} · {check[1]}</li>
        {/each}
      </ul>
    {/if}
    {#if actions.length}
      <div class="ls-answers flush">
        {#each actions as action (action.label)}
          <HaloButton size="small" {...action} />
        {/each}
      </div>
    {/if}
  </div>
</section>
