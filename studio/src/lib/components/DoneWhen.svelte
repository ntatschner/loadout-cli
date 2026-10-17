<script lang="ts">
  // What a run will be judged on: each criterion, how the lead read it, its
  // verdict and the evidence. Something the lead cited that no worker
  // reported is said out loud.
  import Signal from './Signal.svelte';

  interface Criterion {
    text: string;
    verdict?: 'met' | 'unmet' | 'not-attempted' | 'not attempted' | null;
    /** From the project's own done-when rather than the run's. */
    project?: boolean;
    meaning?: string | null;
    evidence?: string[];
    delivered?: string[];
    cited?: string[];
  }

  let { criteria, by = null, goal = null }: { criteria: Criterion[]; by?: string | null; goal?: string | null } = $props();

  const met = $derived(criteria.filter((one) => one.verdict === 'met').length);
  const WORD: Record<string, string> = { met: 'met', unmet: 'unmet', 'not-attempted': 'not attempted', 'not attempted': 'not attempted' };
</script>

<div class="ls-done">
  <div class="head"><b>Done when {met} of {criteria.length} met</b>{#if by}<span>Criteria {by}</span>{/if}</div>
  {#if goal}<p class="goalread">The goal, taken to mean: <i>{goal}</i></p>{/if}
  {#if !criteria.length}
    <p class="goalread">No criteria. Done is the lead’s word for it.</p>
  {:else}
    <ul class="crit">
      {#each criteria as one, index (index)}
        <li>
          <Signal word={WORD[one.verdict ?? ''] ?? 'no verdict'} />
          <span class="t">{one.text}{#if one.project}<span class="ls-chip">project</span>{/if}</span>
          {#if one.meaning}<span class="m">taken to mean: <i>{one.meaning}</i></span>{/if}
          {#each one.evidence ?? [] as evidence, n (n)}<span class="e">{evidence}</span>{/each}
          {#each one.delivered ?? [] as item, n (n)}<span class="e">delivered: {item}</span>{/each}
          {#each one.cited ?? [] as item, n (n)}<span class="e cited">! cited, but no worker reported it: {item}</span>{/each}
        </li>
      {/each}
    </ul>
  {/if}
</div>
