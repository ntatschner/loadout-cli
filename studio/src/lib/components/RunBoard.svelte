<script lang="ts">
  // Team Focus: every live run as a line, the one that needs you first,
  // with the teams along the top. A run's question is answered in its row,
  // with the same Gate, and so the same command, as its own page.
  import Gate from './Gate.svelte';
  import MiniLine from './MiniLine.svelte';
  import Signal from './Signal.svelte';
  import { css } from '../css';
  import { signalOf } from '../signals';
  import type { BoardRun, BoardTeam } from '../modes';

  interface Props {
    runs: BoardRun[];
    teams?: BoardTeam[];
    picked?: string | null;
    team?: string | null;
    label?: string;
    onpick?: (id: string) => void;
    onteam?: (name: string) => void;
  }

  let { runs, teams = [], picked = null, team = null, label = 'Live team runs', onpick = undefined, onteam = undefined }: Props = $props();

  const rank = (word: string) => ({ needs: 0, bad: 1, moving: 2 } as Record<string, number>)[signalOf(word).family] ?? 3;
  const sorted = $derived(runs.slice().sort((a, b) => rank(a.word) - rank(b.word)));
  const money = (value: number) => `$${value.toFixed(2)}`;
</script>

<section class="ls-board" aria-label={label}>
  {#if teams.length}
    <div class="teams" role="group" aria-label="Teams">
      {#each teams as one (one.name)}
        <button type="button" aria-pressed={team === one.name ? 'true' : 'false'} onclick={() => onteam?.(one.name)}>
          <Signal word={one.word || 'idle'} />{one.name}<em>{one.fact ?? ''}</em>
        </button>
      {/each}
    </div>
  {/if}
  <ol aria-label="Runs, the one that needs you first">
    {#each sorted as run, index (run.id)}
      {@const family = signalOf(run.word).family}
      {@const live = family === 'moving' || family === 'needs' || run.word === 'held after this round'}
      <li class={`brow ${family}`} class:picked={picked === run.id} use:css={{ '--i': String(index) }}>
        <button type="button" class="go" aria-pressed={picked === run.id ? 'true' : 'false'} onclick={() => onpick?.(run.id)}>
          <span class="t">{run.goal}</span>
          <span class="x">{[run.team, run.project, run.round ? `round ${run.round}` : null].filter(Boolean).join(' · ')}</span>
        </button>
        <MiniLine rounds={run.rounds ?? []} end={!live} running={live} />
        <Signal word={run.word} />
        <span class="spend">
          <b>{money(run.spent ?? 0)}</b>{run.cap ? ` of $${run.cap.toFixed(0)}` : ' no cap'}
          {#if run.cap && run.projected && run.projected > run.cap}<em>projected over</em>{/if}
        </span>
        {#if run.gate}
          <Gate {...run.gate} run={run.id} compact />
        {/if}
      </li>
    {:else}
      <li class="brow"><span class="x">No team runs here.</span></li>
    {/each}
  </ol>
</section>
