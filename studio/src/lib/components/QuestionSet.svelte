<script lang="ts">
  // The questions an agent asked about an idea, answered in the page: an
  // option as a large chip, or your own words. Nothing is sent until one of
  // the two buttons at the foot is pressed; then each changed answer goes as
  // its own idea answer, and a refine after them when asked for. Answered
  // questions fold to a line, and what you are looking at stays where it is.
  import { onDestroy, tick, untrack } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { lineFor } from '../commandLine';
  import { holdPlace } from '../holdPlace';
  import { aboutLine, acceptRecommendations, changedAnswers, offeredChoices, settledDrafts, standingAnswer, understoodBefore, type Answer, type Question, type Round } from '../ideas';

  interface Props {
    id: string;
    where: string[];
    questions: Question[];
    /** The rounds they were asked in, for what the agent understood when it asked. */
    rounds?: Round[];
    disabledReason?: string;
    /** Sends the answers, then refines when asked; true when every one went. */
    onsend?: (answers: Answer[], refine: boolean) => Promise<boolean>;
    /** Told which questions have an answer typed and not sent, and which were sent and not yet read back. */
    ondraft?: (drafted: string[], sent: string[]) => void;
  }

  let { id, where, questions, rounds = undefined, disabledReason = undefined, onsend = undefined, ondraft = undefined }: Props = $props();

  let drafts = $state<Record<string, string>>({});
  let editing = $state<Record<string, boolean>>({});
  // Sent and not yet read back: shown as answered until the idea is read again.
  let sent = $state<Record<string, string>>({});
  let said = $state('');
  let sending = $state(false);
  let root = $state<HTMLElement | null>(null);
  const name = `q-${Math.random().toString(36).slice(2, 7)}`;

  // A reload brings the saved answers: drafts equal to them are dropped, and so is what was sent.
  // Only the questions are watched: the effect writes the drafts, and must not run again on its own write.
  $effect(() => {
    const now = questions;

    untrack(() => {
      drafts = settledDrafts(now, drafts);
      sent = Object.fromEntries(Object.entries(sent).filter(([question, text]) => now.find((one) => one.id === question)?.answer !== text));
    });
  });

  const answerOf = (question: Question) => sent[question.id] ?? question.answer;
  const changed = $derived(changedAnswers(id, where, questions.map((one) => ({ ...one, answer: answerOf(one) })), drafts));
  const count = $derived(changed.length);

  // The page's Next button goes to what is still open, then to the send button.
  $effect(() => {
    ondraft?.(changed.map((one) => one.question), Object.keys(sent));
  });

  onDestroy(() => ondraft?.([], []));
  const reason = $derived(disabledReason ?? (count ? undefined : 'answer a question first'));
  const open = (question: Question) => !answerOf(question);
  const taken = $derived(acceptRecommendations(questions, drafts, open));
  const takeable = $derived(Object.keys(taken).filter((question) => taken[question] !== drafts[question]).length);
  const takeReason = $derived(disabledReason ?? (takeable ? undefined : 'no recommendations left to take'));

  function draft(question: string, text: string) {
    drafts = { ...drafts, [question]: text };
  }

  function takeAll() {
    drafts = taken;
  }

  function change(question: Question) {
    editing = { ...editing, [question.id]: true };
    draft(question.id, answerOf(question) ?? '');
  }

  /** Sends the saved answer again, which says it still stands and clears its possibly outdated marker. */
  async function stand(question: Question) {
    const again = standingAnswer(id, where, question);

    if (!onsend || sending || !again) {
      return;
    }

    sending = true;
    said = '';

    const ok = await onsend([again], false);

    sending = false;

    if (ok) {
      said = `✓ ${question.id} still stands.`;
    }
  }

  async function send(refine: boolean) {
    if (!onsend || sending) {
      return;
    }

    const going = changed;

    sending = true;
    said = '';

    const ok = await onsend(going, refine);

    sending = false;

    if (ok) {
      const held = root ? holdPlace(root) : null;
      const next = { ...sent };
      const left = { ...drafts };
      const open = { ...editing };

      for (const one of going) {
        next[one.question] = one.answer;
        delete left[one.question];
        open[one.question] = false;
      }

      sent = next;
      drafts = left;
      editing = open;
      said = `✓ ${going.length} answer${going.length === 1 ? '' : 's'} saved${refine ? '. The next round has started: it runs on the daemon, and a toast says when it ends.' : '.'}`;
      await tick();
      held?.();
    }
  }
</script>

<div class="ls-qset" bind:this={root}>
  <div class="qs">
    {#each questions as question (question.id)}
      {@const answer = answerOf(question)}
      {@const answered = !!answer && !editing[question.id]}
      {@const typed = (drafts[question.id] ?? '').trim()}
      {@const offered = offeredChoices(question)}
      {@const own = typed && !offered.choices.includes(typed) ? (drafts[question.id] ?? '') : ''}
      {@const understood = understoodBefore(rounds, question.id)}
      {@const about = aboutLine(question)}
      {#if understood}<p class="understood">Understood as: {understood}</p>{/if}
      <section class={['q', answered ? 'answered' : typed ? 'drafted' : ''].filter(Boolean).join(' ')} aria-labelledby={`${name}-${question.id}`} data-next={`question:${question.id}`} data-anchor={`question:${question.id}`}>
        <span class="who">{[question.id, answered ? 'answered' : typed ? 'your answer, not sent yet' : 'the agent asks'].join(' · ')}</span>
        {#if answered}
          <p class="qt" id={`${name}-${question.id}`}>{question.question}</p>
          {#if about}<p class="about">{about}</p>{/if}
          {#if question.outdated}<p class="outdated">Possibly outdated: {question.outdated}. Answering again clears it.{#if !disabledReason}<button type="button" onclick={() => void stand(question)}>It still stands</button>{/if}</p>{/if}
          <p class="was">You answered: <b>{answer}</b>{#if !disabledReason}<button type="button" onclick={() => change(question)}>Change it</button>{/if}</p>
        {:else}
          <fieldset>
            <legend id={`${name}-${question.id}`}>{question.question}</legend>
            {#if about}<p class="about">{about}</p>{/if}
            {#if question.outdated}<p class="outdated">Possibly outdated: {question.outdated}. Answering clears it.</p>{/if}
            {#if question.why}<p class="why">{question.why}</p>{/if}
            {#if offered.choices.length}
              <div class="opts">
                {#each offered.choices as option, index (index)}
                  <label class="opt"><input type="radio" name={`${name}-${question.id}-o`} value={option} checked={typed === option} onchange={() => draft(question.id, option)} />{option}{#if offered.recommended === index}<span class="rec">recommended</span>{/if}</label>
                {/each}
              </div>
            {/if}
            {#if offered.note && offered.recommended !== null}
              <p class="why">Why it recommends {offered.choices[offered.recommended]}: {offered.note}</p>
            {/if}
            <div class="own">
              <label for={`${name}-${question.id}-w`}>Or in your own words</label>
              <textarea class="ls-text" id={`${name}-${question.id}-w`} rows="2" placeholder="Or say it in your own words" value={own} oninput={(event) => draft(question.id, (event.currentTarget as HTMLTextAreaElement).value)}></textarea>
            </div>
          </fieldset>
        {/if}
      </section>
    {/each}
  </div>

  <div class="send" data-anchor="send-answers">
    <p class="count">{count ? `${count} answer${count === 1 ? '' : 's'} ready to send.` : 'Answer what you can. Nothing is sent until you press a button.'}</p>
    <div class="ls-answers flush">
      <HaloButton label={sending ? 'Sending…' : 'Send answers and refine'} next="send-answers" kind="needs" solid={!reason} glyph="✓" cmd={`${count} × loadout idea answer, then ${lineFor('idea refine', [id, ...where])}`} disabledReason={reason} onpress={() => void send(true)} />
      <HaloButton label="Only save the answers" cmd={`${count} × loadout idea answer`} disabledReason={reason} onpress={() => void send(false)} />
      <HaloButton label="Accept all recommendations" cmd={`fills ${takeable} answer${takeable === 1 ? '' : 's'}; sends nothing`} disabledReason={takeReason} onpress={takeAll} />
    </div>
    {#if count}
      <details>
        <summary>The commands, one per answer</summary>
        {#each changed as one (one.question)}<code>{one.line}</code>{/each}
      </details>
    {/if}
    <p class="ls-said held" role="status">{said}</p>
  </div>
</div>
