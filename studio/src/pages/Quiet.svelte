<script lang="ts">
  // Minimal's Home: one quiet column of what is live, sessions and team
  // runs together, what needs you first. Opening one gives it the whole
  // screen: a session as just its terminal, a run as just its question, its
  // shape and its done-when. Escape gives the column back.
  import { untrack } from 'svelte';
  import DoneWhen from '../lib/components/DoneWhen.svelte';
  import FocusFrame from '../lib/components/FocusFrame.svelte';
  import Gate from '../lib/components/Gate.svelte';
  import QuietList from '../lib/components/QuietList.svelte';
  import RunShape from '../lib/components/RunShape.svelte';
  import { teamApi, type Outcome } from '../lib/api';
  import { studio } from '../lib/state.svelte';
  import { wordFor } from '../lib/signals';
  import { answerGate } from '../lib/teamsAct';
  import { criteriaOf, gateOf, runWord, shapeOf } from '../lib/teamsData';
  import type { QuietItem } from '../lib/modes';

  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);
  let outcomes = $state<Outcome[] | null>(null);

  const watch = $derived(studio.watchOnly ? 'Studio is only watching: type the command in a terminal' : undefined);

  const items: QuietItem[] = $derived([
    ...studio.lines
      .filter((line) => line.kind === 'session' && line.stage !== 4)
      .map((line) => ({
        id: line.ref,
        kind: 'session' as const,
        title: line.title,
        word: wordFor(line, studio.idleAfter),
        where: line.session?.project ?? null,
        ask: line.waiting ? (line.approval ? 'Asks for approval. Answer it in the terminal.' : 'Waiting for you. Answer it in the terminal.') : '',
      })),
    ...studio.runs
      .filter((run) => run.running)
      .map((run) => ({ id: run.id, kind: 'team run' as const, title: run.goal, word: runWord(run), where: run.team, ask: run.gates[0]?.brief ?? run.gates[0]?.question ?? '' })),
  ]);

  const run = $derived(open?.item.kind === 'team run' ? (studio.runs.find((one) => one.id === open!.item.id) ?? null) : null);
  const session = $derived(open?.item.kind === 'session' ? (studio.lines.find((line) => line.kind === 'session' && line.ref === open!.item.id) ?? null) : null);

  // Something else, the ModeBar's waiting count or a jump, may ask for one to
  // be opened. One just started is not listed yet, so the ask waits for it.
  $effect(() => {
    const wanted = studio.focusing;
    const item = wanted ? items.find((one) => one.id === wanted.id && one.kind === wanted.kind) : undefined;

    if (item) {
      untrack(() => {
        studio.focusing = null;
        show(item, null);
      });
    }
  });

  function show(item: QuietItem, from: DOMRect | null) {
    open = { item, from };
    outcomes = null;

    if (item.kind === 'team run') {
      teamApi.outcomes(item.id).then((read) => (outcomes = read), () => (outcomes = null));
    }
  }
</script>

<h1 class="ls-vh">What is live</h1>
<div class="quietwrap">
  <QuietList {items} onopen={show} />
</div>

{#if open}
  {#key open.item.kind + open.item.id}
    {#if open.item.kind === 'session'}
      <FocusFrame
        title={open.item.title}
        sub={[session?.session?.project, session?.session?.agent === 'codex' ? 'Codex' : 'Claude Code'].filter(Boolean).join(' · ')}
        word={open.item.word}
        from={open.from}
        onclose={() => (open = null)}
      >
        {#await import('../lib/components/SessionPane.svelte')}
          <p class="calm" role="status">Opening the terminal…</p>
        {:then pane}
          <!-- The keyboard goes to the terminal as soon as it is on the page. -->
          <pane.default id={open.item.id} onready={(handle) => handle?.focus()} />
        {/await}
      </FocusFrame>
    {:else if run}
      {@const gate = run.gates[0]}
      {@const shape = shapeOf(run)}
      <FocusFrame title={run.goal} sub={[run.team, run.project, `round ${run.rounds}`].filter(Boolean).join(' · ')} word={runWord(run)} from={open.from} onclose={() => (open = null)}>
        <div class="qrun">
          {#if gate}
            <Gate {...gateOf(run, gate)} run={run.id} disabledReason={watch} onanswer={(send) => answerGate(run.id, gate.id, send)} />
          {/if}
          <RunShape rounds={shape.rounds} end={shape.end} />
          {#if outcomes}
            <DoneWhen criteria={criteriaOf(outcomes)} goal={run.goalUnderstood} />
          {/if}
          <p class="calm small">Everything else about it is on its page: <a href={`#teams/${encodeURIComponent(run.id)}`} onclick={() => studio.setMode('default')}>open it in full</a>.</p>
        </div>
      </FocusFrame>
    {:else}
      <FocusFrame title={open.item.title} word="ended" from={open.from} onclose={() => (open = null)}>
        <p class="calm">That run has ended. <a href={`#teams/${encodeURIComponent(open.item.id)}`}>Its page</a> says how.</p>
      </FocusFrame>
    {/if}
  {/key}
{/if}
