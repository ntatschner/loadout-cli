<script lang="ts">
  // Runs that ended short of their done-when, each with what Loadout
  // suggests and why, and every other action open to it. Nothing is done
  // until a person confirms: the suggestion is picked first, the person can
  // pick another, and the confirm button shows the command it runs. A raise
  // is filled in with the same again, beside what the run used, and can be
  // changed before confirming; a refusal against a rule's cap says by how
  // much and where the rule's page is.
  import { untrack } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { choiceFor, gotTo, notReady, resolveLine, type BlockedChoice, type BlockedRead } from '../blocked';

  interface Props {
    runs: BlockedRead[];
    disabledReason?: string;
    /** Resolves to what the command said, or throws with why it refused. */
    onresolve: (run: BlockedRead, choice: BlockedChoice) => Promise<string>;
  }

  let { runs, disabledReason = undefined, onresolve }: Props = $props();

  // One choice per run, kept by id so a reread does not throw away what was
  // being typed.
  let choices = $state<Record<string, BlockedChoice>>({});
  let said = $state<Record<string, { ok: boolean; text: string }>>({});
  let busy = $state<string | null>(null);

  // Seeded here rather than as the list draws, because a template may not
  // write state; only a run not seen before gets one.
  $effect(() => {
    for (const run of runs) {
      if (!untrack(() => choices[run.id])) {
        choices[run.id] = choiceFor(run);
      }
    }
  });

  const choice = (run: BlockedRead) => choices[run.id] ?? choiceFor(run);

  async function confirm(run: BlockedRead) {
    busy = run.id;

    try {
      said[run.id] = { ok: true, text: await onresolve(run, $state.snapshot(choice(run))) };
    } catch (error) {
      said[run.id] = { ok: false, text: (error as Error).message };
    } finally {
      busy = null;
    }
  }
</script>

<div class="ls-blocked">
  {#if !runs.length}
    <p class="calm">No runs are waiting on you: every run that ended has been dealt with or got there.</p>
  {/if}
  <ul aria-label="Runs that ended short of their done-when">
    {#each runs.filter((one) => choices[one.id]) as run (run.id)}
      {@const picked = choice(run)}
      {@const why = notReady(run, picked)}
      <li>
        <div class="head">
          <b>{run.id}</b>
          <span>{run.team}{run.project ? ` on ${run.project}` : ''}{run.task ? `, task ${run.task}` : ''}</span>
          <span class="kind">{run.kind}</span>
        </div>
        <p class="goal">{run.goal}</p>
        <p class="calm small">{run.ended || (run.kind === 'dead' ? 'Its process went away while its journal still said it was running.' : 'It did not say how it ended.')} · {gotTo(run)}{run.origin ? ` · started by rule ${run.origin}` : ''}{run.chain > 1 ? ` · run ${run.chain} in a row on this task` : ''}</p>
        {#if run.lead?.blocker || run.lead?.questions.length}
          <p class="lead">
            {#if run.lead.blocker}Blocked on: {run.lead.blocker}{run.lead.unblockedBy ? `. It said this would unblock it: ${run.lead.unblockedBy}` : ''}.{/if}
            {#each run.lead.questions as question (question)} Asked: {question}{/each}
          </p>
        {/if}
        <p class="suggested"><b>Suggested: {run.suggested.label}.</b> {run.suggested.reason}</p>

        <fieldset>
          <legend>What to do</legend>
          {#each run.offered as one (one.action)}
            <label class="pick">
              <input type="radio" name={`blocked-${run.id}`} value={one.action} bind:group={choices[run.id].action} />
              {one.label}{one.action === run.suggested.action ? ' (suggested)' : ''}
            </label>
          {/each}
        </fieldset>

        {#if picked.action === 'raise' && run.raise}
          <div class="ls-field">
            <label class="lab" for={`raise-${run.id}`}>{run.raise.cap === 'rounds' ? 'Rounds for the next run' : 'What the next run may spend ($)'}</label>
            <input id={`raise-${run.id}`} class="ls-text" type="text" inputmode="decimal" autocomplete="off" bind:value={choices[run.id].figure} />
            <span class="calm small">The same again. {run.raise.cap === 'rounds' ? `It used ${run.raise.used} of ${run.raise.figure} round(s).` : `It spent $${run.raise.used.toFixed(2)} of $${run.raise.figure.toFixed(2)}.`}</span>
          </div>
        {/if}

        {#if picked.action === 'edit-requeue'}
          <div class="ls-field">
            <label class="lab" for={`title-${run.id}`}>New title</label>
            <input id={`title-${run.id}`} class="ls-text" type="text" autocomplete="off" bind:value={choices[run.id].title} />
          </div>
          <div class="ls-field">
            <label class="lab" for={`detail-${run.id}`}>New detail</label>
            <textarea id={`detail-${run.id}`} class="ls-text" rows="3" spellcheck="false" bind:value={choices[run.id].detail}></textarea>
          </div>
          <div class="ls-field">
            <label class="lab" for={`done-${run.id}`}>Done when, one per line (replaces what it has)</label>
            <textarea id={`done-${run.id}`} class="ls-text" rows="3" spellcheck="false" bind:value={choices[run.id].doneWhen}></textarea>
          </div>
        {/if}

        <div class="ls-field">
          <label class="lab" for={`answer-${run.id}`}>{picked.action === 'carry-on' || picked.action === 'raise' ? 'What to tell the next lead' : 'A note, kept on the run'}</label>
          <textarea id={`answer-${run.id}`} class="ls-text" rows="2" spellcheck="false" bind:value={choices[run.id].answer}></textarea>
        </div>

        <div class="ls-answers">
          <HaloButton
            label="Confirm"
            size="small"
            cmd={resolveLine(run, picked)}
            disabledReason={disabledReason ?? (busy === run.id ? 'working on it' : (why ?? undefined))}
            onpress={() => void confirm(run)}
          />
        </div>
        {#if said[run.id]}
          <p class={said[run.id].ok ? 'ls-said' : 'refused'} role="status">{said[run.id].ok ? '✓ ' : ''}{said[run.id].text}</p>
        {/if}
      </li>
    {/each}
  </ul>
</div>

<style>
  .ls-blocked ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: var(--ls-space-4, 1rem);
  }

  .ls-blocked li {
    border: 1px solid var(--ls-line, currentColor);
    border-radius: var(--ls-radius, 6px);
    padding: var(--ls-space-3, 0.75rem);
    max-width: 76ch;
  }

  .head {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: baseline;
  }

  .kind {
    text-transform: uppercase;
    font-size: 0.8em;
    letter-spacing: 0.04em;
  }

  .goal,
  .lead,
  .suggested {
    margin: 0.4rem 0;
  }

  fieldset {
    border: 0;
    margin: 0.5rem 0;
    padding: 0;
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem 1rem;
  }

  legend {
    font-weight: 600;
    margin-bottom: 0.25rem;
  }

  .ls-field input.ls-text {
    max-width: 16ch;
  }

  .refused {
    color: var(--ls-danger, inherit);
  }
</style>
