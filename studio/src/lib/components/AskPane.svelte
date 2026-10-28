<script lang="ts">
  // The palette's conversation with Loadout: each request and its answer,
  // newest last. An answer says who gave it; a near miss can be asked as it
  // stands; a plan is a card to approve.
  import AskPlanCard from './AskPlanCard.svelte';
  import { usd, type AskPlan, type AskTurn } from '../ask';

  interface Props {
    turns: AskTurn[];
    asking: boolean;
    /** Asks a near miss as it stands. */
    onrewrite: (rewrite: string) => void;
    /** Asks the agent the request as it was typed. */
    onagent: (query: string) => void;
    approve: (plan: AskPlan, team: string | null, cap: string) => Promise<string>;
    /** Called once a plan is approved. */
    onapproved?: () => void;
    onclear: () => void;
  }

  let { turns, asking, onrewrite, onagent, approve, onapproved = undefined, onclear }: Props = $props();
</script>

<div class="ls-ask" aria-live="polite">
  {#if !turns.length}
    <p class="ls-pempty">
      Ask in plain words, then press Enter. Loadout answers what it can from its own records:
      what you spent, which tasks are easy or need work first, or what to queue and start.
    </p>
  {/if}
  {#each turns as turn (turn.id)}
    <section class="turn">
      <p class="q"><span aria-hidden="true">?</span> {turn.query}</p>
      {#if turn.failed}
        <p class="failed" role="alert">{turn.failed}</p>
      {:else if !turn.reply}
        <p class="wait">Asking…</p>
      {:else}
        {@const reply = turn.reply}
        <p class="badge">
          {#if reply.answeredBy === 'agent'}Answered by the agent{#if reply.agentCostUsd !== null}, for {usd(reply.agentCostUsd)}{/if}{:else}Answered by Loadout{/if}
        </p>
        <p class="a">{reply.summary}</p>
        {#if reply.spend}
          <ul class="models" aria-label="By model">
            {#each reply.spend.models as model (model.model)}
              <li><span>{model.model}</span><span>{model.tokens.toLocaleString('en-GB')} tokens</span><span>{model.usd === null ? 'no price' : usd(model.usd)}</span></li>
            {/each}
          </ul>
          {#if reply.spend.caveat}<p class="caveat">{reply.spend.caveat}</p>{/if}
        {/if}
        {#if reply.tasks.length}
          <ol class="tasks">
            {#each reply.tasks as task (`${task.project}/${task.id}`)}
              <li><span class="says">{task.title} <span class="where">{task.id} · {task.project || 'workspace'}</span></span>{#if task.reasons.length}<span class="why">{task.reasons.join('; ')}</span>{/if}</li>
            {/each}
          </ol>
        {/if}
        {#if reply.plan}
          <AskPlanCard plan={reply.plan} {approve} {onapproved} />
        {/if}
        {#if reply.nearMisses.length}
          <ul class="near" aria-label="Closest requests Loadout can answer">
            {#each reply.nearMisses as near (near.rewrite)}
              <li><button type="button" class="ls-opt" onclick={() => onrewrite(near.rewrite)}><span class="g" aria-hidden="true">?</span><span class="t">{near.rewrite}</span></button></li>
            {/each}
            {#if reply.agentOffer}
              <li>
                <button type="button" class="ls-opt" disabled={asking} onclick={() => onagent(turn.query)}>
                  <span class="g" aria-hidden="true">✦</span><span class="t">Ask the agent instead, for about {usd(reply.agentOffer.estimatedUsd)}</span>
                  <span class="s">{reply.agentOffer.basis}. It only reads; anything it proposes comes back as a plan.</span>
                </button>
              </li>
            {/if}
          </ul>
        {/if}
      {/if}
    </section>
  {/each}
  {#if turns.length}
    <button type="button" class="clear" onclick={onclear} disabled={asking}>Clear the conversation</button>
  {/if}
</div>

<style>
  .ls-ask { padding: 0 18px; }
  .turn { margin: 12px 0; }
  .q { margin: 0; font-weight: 500; color: var(--ink-strong); }
  .q span { color: var(--accent); }
  .badge { margin: 4px 0 2px; font-size: 11px; letter-spacing: .14em; text-transform: uppercase; color: var(--subtle); }
  .a { margin: 0; }
  .wait { color: var(--muted); }
  .failed, .caveat { color: var(--needs-text); }
  .models { list-style: none; margin: 6px 0; padding: 0; font-family: var(--font-mono); font-size: 12px; }
  .models li { display: grid; grid-template-columns: 1fr auto 9ch; gap: 12px; }
  .models li span:last-child { text-align: right; }
  .tasks { margin: 6px 0; padding-left: 20px; }
  .tasks li { margin: 4px 0; }
  .says, .why { display: block; }
  .where, .why { font-size: 12.5px; color: var(--muted); }
  .near { list-style: none; margin: 6px 0; padding: 0; }
  .clear { margin: 4px 0 8px; background: none; border: 0; padding: 0; color: var(--muted); text-decoration: underline; cursor: pointer; font: inherit; font-size: 12.5px; }
</style>
