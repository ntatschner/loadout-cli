<script lang="ts">
  // An idea's plan, read: which plan it is and where it lives, the warnings
  // that matter at the moment of confirming, then the document itself. Read
  // only; the Markdown is what 'idea plan' gives, drawn and never rewritten.
  import Markdown from './Markdown.svelte';
  import { SignedOut } from '../api';
  import { planSaid, readPlan, type PlanRead } from '../plans';
  import { studio } from '../state.svelte';

  interface Props {
    id: string;
    /** The list it is on, as the idea commands name it. */
    where: string[];
    /** For a preview, the done-when words accepting would send. */
    doneWhen?: string[];
    /** Told the plan once it is read, or null when it could not be. */
    onread?: (plan: PlanRead | null) => void;
  }

  let { id, where, doneWhen = [], onread = undefined }: Props = $props();

  let plan = $state<PlanRead | null>(null);
  let problem = $state<string | null>(null);

  // Keyed on the words, not the arrays: a parent that makes a new array with
  // the same words on each draw must not read the plan again.
  const asking = $derived(JSON.stringify({ id, where, doneWhen }));

  $effect(() => {
    const asked = JSON.parse(asking) as { id: string; where: string[]; doneWhen: string[] };
    let current = true;

    plan = null;
    problem = null;

    readPlan(asked.id, asked.where, asked.doneWhen).then(
      (read) => {
        if (current) {
          plan = read;
          onread?.(read);
        }
      },
      (error) => {
        if (!current) return;

        if (error instanceof SignedOut) {
          studio.signedOut = true;
        } else {
          problem = (error as Error).message;
        }

        onread?.(null);
      },
    );

    return () => (current = false);
  });
</script>

<div class="ls-plan" data-plan-state={plan?.state ?? (problem ? 'failed' : 'reading')}>
  {#if problem}
    <p class="offline" role="alert">{problem}</p>
  {:else if !plan}
    <p class="calm" role="status">Reading the plan…</p>
  {:else}
    <p class="said" role={plan.state === 'missing' || plan.changed ? 'alert' : undefined}>
      <span class={`ls-tag ${plan.state === 'preview' ? '' : plan.state === 'missing' || plan.changed ? 'needs' : ''}`}>{plan.state === 'preview' ? 'preview' : plan.state === 'missing' ? 'missing' : plan.changed ? 'edited' : 'written'}</span>
      {planSaid(plan)}
    </p>
    {#if plan.undecided?.length}
      <p role="note" class="warn"><span class="ls-tag needs">no option chosen</span> {plan.undecided.join(', ')} {plan.undecided.length === 1 ? 'goes' : 'go'} in with no option chosen, and the plan says so where it is written.</p>
    {/if}
    {#if plan.gapFilled?.length}
      <p role="note" class="warn"><span class="ls-tag needs">gap-filled</span> The {plan.gapFilled.join(', ')} here {plan.gapFilled.length === 1 ? 'was' : 'were'} written in by a backfill pass. Confirming accepts {plan.gapFilled.length === 1 ? 'it' : 'them'}.</p>
    {/if}
    {#if plan.markdown}
      <div class="doc"><Markdown source={plan.markdown} /></div>
    {/if}
  {/if}
</div>

<style>
  .ls-plan .said { display: flex; gap: var(--space-2); align-items: baseline; flex-wrap: wrap; }
  .ls-plan .doc { margin-top: var(--space-4); padding-top: var(--space-3); border-top: 1px solid var(--rule); }
</style>
