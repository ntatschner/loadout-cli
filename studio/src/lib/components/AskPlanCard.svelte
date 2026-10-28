<script lang="ts">
  // One plan from Ask Loadout: every step with why its task was picked, the
  // team and the cap, and Approve. Nothing starts until Approve is pressed,
  // and Approve stays held while a team or a needed cap is missing.
  import HaloButton from './HaloButton.svelte';
  import { approveReason, runNowWords, startingCap, type AskPlan } from '../ask';

  interface Props {
    plan: AskPlan;
    /** Carries the plan out and says what happened, or throws with why not. */
    approve: (plan: AskPlan, team: string | null, cap: string) => Promise<string>;
    /** Called once a plan is approved, so the palette can take focus back. */
    onapproved?: () => void;
  }

  let { plan, approve, onapproved = undefined }: Props = $props();

  const uid = `ls-ap-${Math.random().toString(36).slice(2, 7)}`;
  // Seeded once from the plan; edited by the person from then on.
  // svelte-ignore state_referenced_locally
  let team = $state(plan.team ?? '');
  // svelte-ignore state_referenced_locally
  let cap = $state(startingCap(plan));
  let busy = $state(false);
  let done = $state<string | null>(null);
  let failed = $state<string | null>(null);

  const starts = $derived(plan.steps.some((step) => step.kind === 'start-team'));
  const reason = $derived(done ? 'Approved.' : busy ? 'Starting…' : approveReason(plan, team || null, cap));

  const command = $derived(
    plan.shape === 'terminals'
      ? `opens ${plan.steps.length} terminal(s), each with its task`
      : starts
        ? `$ loadout team queue run-now ${runNowWords(plan, team || '<team>', cap || '<cap>').join(' ')}`
        : `$ loadout team queue add ${team || '<team>'} <task> --project <project>, for each`,
  );

  async function press() {
    busy = true;
    failed = null;

    try {
      done = await approve(plan, team || null, cap);
      // Approve is held from here on, so focus left on it would go nowhere.
      // A refusal leaves it where it is: the button can be pressed again.
      onapproved?.();
    } catch (error) {
      failed = error instanceof Error ? error.message : String(error);
    } finally {
      busy = false;
    }
  }
</script>

<div class="ls-askplan" role="group" aria-labelledby={`${uid}-h`}>
  <h4 id={`${uid}-h`}>Plan: nothing starts until you approve it</h4>
  <ol>
    {#each plan.steps as step, index (index)}
      <li>
        <span class="says">{step.says}</span>
        {#if step.reasons.length}<span class="why">{step.reasons.join('; ')}</span>{/if}
      </li>
    {/each}
  </ol>
  {#if plan.left.length}
    <p class="left">Left out: {plan.left.join('; ')}</p>
  {/if}
  <div class="fields">
    {#if plan.shape === 'team-queue' && !plan.team}
      <label>
        <span>Team</span>
        <select name="ask-team" bind:value={team} disabled={!!done}>
          <option value="">Choose a team</option>
          {#each plan.teamChoices as choice (choice)}<option value={choice}>{choice}</option>{/each}
        </select>
      </label>
    {/if}
    {#if plan.capped}
      <label>
        <span>Spending cap, US dollars, for the whole plan</span>
        <input name="ask-cap" inputmode="decimal" placeholder={plan.cap === null && !plan.capNone ? 'Required: team-budget is not set' : ''} bind:value={cap} disabled={!!done} autocomplete="off" />
      </label>
    {/if}
  </div>
  <HaloButton label="Approve" size="small" cmd={command} disabledReason={reason ?? undefined} onpress={() => void press()} />
  {#if done}<p class="done" role="status">{done}</p>{/if}
  {#if failed}<p class="failed" role="alert">{failed}</p>{/if}
</div>

<style>
  .ls-askplan { margin: 8px 0 0; padding: 10px 14px; border-radius: var(--radius-chip); box-shadow: inset 0 0 0 1px var(--rule); }
  h4 { margin: 0 0 6px; font-size: 12px; font-weight: 500; letter-spacing: .14em; text-transform: uppercase; color: var(--muted); }
  ol { margin: 0 0 8px; padding-left: 20px; }
  li { margin: 4px 0; }
  .says { display: block; }
  .why, .left { display: block; font-size: 12.5px; color: var(--muted); }
  .fields { display: flex; flex-wrap: wrap; gap: 8px 16px; margin: 6px 0 10px; }
  label { display: grid; gap: 2px; font-size: 12.5px; color: var(--ink-soft); }
  input { width: 14ch; }
  .done { color: var(--ink-soft); }
  .failed { color: var(--needs-text); }
</style>
