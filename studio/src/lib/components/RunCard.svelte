<script lang="ts">
  // One run in a list: what it is for, its state, its round and its spend,
  // why it wants you, and what it asks answered in place.
  import Gate from './Gate.svelte';
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import Spend from './Spend.svelte';
  import { signalOf } from '../signals';
  import type { Act, GateShown, RunSummary } from '../teams';

  interface Props {
    run: RunSummary;
    href?: string;
    gate?: GateShown | null;
    reasons?: { text: string; clears: string }[];
    actions?: Act[];
    /** What is known of the process driving it, when that is worth saying (livenessOf). */
    badge?: string | null;
  }

  let { run, href = undefined, gate = null, reasons = [], actions = [], badge = null }: Props = $props();

  const family = $derived(signalOf(run.word).family);
  const facts = $derived(
    [run.team, run.project, run.posture, run.round ? `round ${run.round}${run.rounds ? ` of ${run.rounds}` : ''}` : null, run.elapsed, run.id]
      .filter(Boolean)
      .join(' · '),
  );
</script>

<article class={`ls-run ${family}`} aria-label={run.goal}>
  <div class="top">
    {#if href}<a class="goal" {href}>{run.goal}</a>{:else}<span class="goal">{run.goal}</span>{/if}
    <Signal word={run.word} />
    {#if badge}<span class="ls-liveness" role="status">{badge}</span>{/if}
  </div>
  <span class="facts">{facts}</span>
  {#if run.spent !== undefined}
    <Spend spent={run.spent} cap={run.cap} uncapped={run.uncapped} projected={run.projected} rate={run.rate} />
  {/if}
  {#if reasons.length}
    <ul class="why">
      {#each reasons as reason, index (index)}
        <li>{reason.text} <span>· clears when {reason.clears}</span></li>
      {/each}
    </ul>
  {/if}
  {#if gate}
    <Gate {...gate} run={run.id} compact />
  {/if}
  {#if actions.length}
    <div class="acts">
      {#each actions as action (action.label)}
        <HaloButton size="small" {...action} />
      {/each}
    </div>
  {/if}
</article>
