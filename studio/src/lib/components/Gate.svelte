<script lang="ts">
  // A question a run stopped to ask, answered where it is shown. Each kind
  // has its own answers and its own command: the lead's question (its
  // options, the recommended one solid, and Think again), its readings of
  // the done-when, a brief in a manual run (in a box you can change before it
  // goes), the proposed done-when (in a box), a confirmation or a permission
  // (yes or no), and its budget (raise it, take the cap off, or a figure).
  import { untrack } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { GATE_WORDS, gateButtons, type GateAnswer, type GateKind } from '../teams';

  interface Props {
    kind: GateKind;
    run: string;
    gate?: string | null;
    node?: string | null;
    /** When it asked, as words. */
    asked?: string | null;
    question: string;
    options?: string[];
    recommendation?: string | null;
    timer?: string | null;
    /** readings: [criterion, how the lead reads it]. */
    readings?: [string, string][];
    brief?: string;
    proposal?: string[];
    /** proposal: the project's own criteria, held whatever is answered. */
    held?: string[];
    compact?: boolean;
    disabledReason?: string;
    /** Sends the answer; resolves false where it was not taken, and the Gate stays open. */
    onanswer?: (answer: GateAnswer) => Promise<boolean | void> | boolean | void;
  }

  let {
    kind,
    run,
    gate = null,
    node = null,
    asked = null,
    question,
    options = [],
    recommendation = null,
    timer = null,
    readings = [],
    brief = '',
    proposal = [],
    held = [],
    compact = false,
    disabledReason = undefined,
    onanswer = undefined,
  }: Props = $props();

  const id = `ls-q-${Math.random().toString(36).slice(2, 8)}`;
  // The box starts as the lead wrote it, then holds what you type.
  let typed = $state(untrack(() => (kind === 'brief' ? brief : proposal.join('\n'))));
  let figure = $state('');
  let reason = $state('');
  let said = $state<string | null>(null);
  let sending = $state(false);

  const buttons = $derived(gateButtons({ kind, run, gate, question, options, recommendation, brief, proposal }, typed, figure, reason));
  const who = $derived([node, GATE_WORDS[kind], asked].filter(Boolean).join(' · '));
  const shows = $derived(!!recommendation && !['confirm', 'permission', 'outward', 'merge', 'brief'].includes(kind));

  async function press(button: (typeof buttons)[number]) {
    if (sending) {
      return;
    }

    sending = true;

    try {
      const taken = await onanswer?.(button.send);

      if (taken !== false) {
        said = button.said;
      }
    } finally {
      sending = false;
    }
  }
</script>

<section class={`ls-gate ${kind}`} class:compact class:answered={!!said} role="group" aria-labelledby={id}>
  <span class="who">{who}</span>
  <p class="q" {id}>{question}</p>

  {#if kind === 'readings' && readings.length}
    <ul class="readings">
      {#each readings as [criterion, meaning], index (index)}
        <li><span class="c">“{criterion}”</span><span class="m">read as: {meaning}</span></li>
      {/each}
    </ul>
  {/if}

  {#if kind === 'brief' || kind === 'proposal'}
    <div class="ls-field wide">
      <label class="lab" for={`${id}-box`}>{kind === 'brief' ? `The brief ${node ?? 'the worker'} will be given` : 'One criterion per line'}</label>
      <textarea id={`${id}-box`} name={`${id}-box`} class="ls-text" rows={kind === 'brief' ? 6 : 4} spellcheck="false" autocomplete="off" readonly={!!said} bind:value={typed}></textarea>
      <span class="hint">
        {kind === 'brief'
          ? 'Change it and send it, and that is what the worker is given. The journal keeps the lead’s words and yours.'
          : 'Empty the box and the run carries on with nothing checking it.'}
      </span>
    </div>
    {#if kind === 'proposal' && held.length}
      <p class="held"><b>Held as well, from the project: </b>{held.join('; ')}. They cannot be unticked here.</p>
    {/if}
  {/if}

  {#if shows}
    <p class="rec">{kind === 'budget' ? 'Loadout offers first: ' : 'The lead recommends: '}<b>{recommendation}</b>.</p>
  {/if}
  {#if timer}<p class="timer">{timer}</p>{/if}

  {#if !said}
    <div class="ls-answers">
      {#each buttons as button (button.label)}
        <HaloButton
          label={button.label}
          kind={button.kind}
          solid={button.solid}
          glyph={button.glyph}
          size={compact ? 'small' : ''}
          cmd={button.cmd}
          {disabledReason}
          onpress={() => void press(button)}
        />
      {/each}
    </div>

    {#if kind === 'budget'}
      <div class="ls-field figure">
        <label class="lab" for={`${id}-figure`}>Or a figure of your own, in US dollars</label>
        <input id={`${id}-figure`} name={`${id}-figure`} class="ls-text" type="text" placeholder="60, or none" autocomplete="off" bind:value={figure} />
      </div>
    {:else if kind !== 'brief' && kind !== 'proposal'}
      <div class="ls-field wide reason">
        <label class="lab" for={`${id}-reason`}>Your reason</label>
        <input id={`${id}-reason`} name={`${id}-reason`} class="ls-text" type="text" placeholder="Optional: why, in a sentence the journal keeps" autocomplete="off" bind:value={reason} />
      </div>
    {/if}
  {/if}

  {#if kind === 'readings' && !said}
    <p class="ls-note">To say what you meant instead, say something to the lead before you answer: it reads it at the start of the round.</p>
  {/if}
  <p class="ls-said" role="status">{said ? `✓ ${said}` : ''}</p>
</section>
