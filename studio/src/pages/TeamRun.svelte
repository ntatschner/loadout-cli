<script lang="ts">
  // One run: its head, its question at full size if it is waiting, its shape,
  // what done means for it and who is on it, its spend and what can be done
  // to it, and five depths below on keys 1 to 5. Below 1024px the side
  // column goes under the main one.
  import { untrack } from 'svelte';
  import Crew from '../lib/components/Crew.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import DoneWhen from '../lib/components/DoneWhen.svelte';
  import Gate from '../lib/components/Gate.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Journal from '../lib/components/Journal.svelte';
  import Outbox from '../lib/components/Outbox.svelte';
  import Patch from '../lib/components/Patch.svelte';
  import RunDepths from '../lib/components/RunDepths.svelte';
  import RunNumbers from '../lib/components/RunNumbers.svelte';
  import RunShape from '../lib/components/RunShape.svelte';
  import SayToNode from '../lib/components/SayToNode.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import Spend from '../lib/components/Spend.svelte';
  import { readJson, teamApi, type NodeDiff, type Outcome, type Run, type StreamStep } from '../lib/api';
  import { diffLines, type OutboxRead } from '../lib/files';
  import { runJob } from '../lib/jobs';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { answerGate, carryOn, close, forget, hold, pickUp, runLines, setBudget, stop, tellLead } from '../lib/teamsAct';
  import { criteriaOf, crewOf, duration, gateOf, livenessOf, runWord, shapeOf, summaryOf, unowned } from '../lib/teamsData';
  import { money, type JournalLine } from '../lib/teams';

  let { id, onagain = undefined }: { id: string; onagain?: (run: Run) => void } = $props();

  const run = $derived(studio.runs.find((one) => one.id === id) ?? null);
  const summary = $derived(run ? summaryOf(run) : null);
  const gate = $derived(run?.gates[0] ?? null);
  const shape = $derived(run ? shapeOf(run) : null);

  let depth = $state(0);
  let node = $state<string | null>(null);
  let outcomes = $state<Outcome[] | null>(null);
  let problem = $state<string | null>(null);
  let budget = $state('');
  let changing = $state(false);
  let forgetting = $state(false);
  let ending = $state(false);
  const badge = $derived(run ? livenessOf(run) : null);

  /** End run: straight away where the owner is known to be gone, asked first where it only looks gone. */
  function endRun(target: Run) {
    if (target.liveness === 'possibly-not-responding') {
      ending = true;
    } else {
      void close(target.id, false);
    }
  }

  /* ---------- reads, again whenever the run moves on ---------- */

  const moved = $derived(run ? `${run.rounds}/${run.running}/${run.gates.length}/${run.turns?.length ?? 0}/${run.outcome}` : '');

  $effect(() => {
    void moved;

    untrack(() => {
      teamApi.outcomes(id).then((read) => (outcomes = read), () => (outcomes = null));
    });
  });

  const criteria = $derived(criteriaOf(outcomes ?? []));

  /* ---------- What it said: the journal live, or one node's stream ---------- */

  let journal = $state<JournalLine[]>([]);
  let stream = $state<StreamStep[]>([]);
  const clock = (at: string) => new Date(at).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });

  $effect(() => {
    if (depth !== 0 || node) {
      return;
    }

    journal = [];
    const seen = new Map<string, number>();

    return teamApi.journal(id, (entry) => {
      // Repeats are counted rather than listed again.
      const last = journal[journal.length - 1];

      if (last && seen.get(entry.what) === journal.length - 1) {
        journal[journal.length - 1] = { ...last, repeats: (last.repeats ?? 1) + 1 };

        return;
      }

      seen.set(entry.what, journal.length);
      journal.push({ time: clock(entry.at), text: entry.what, kind: entry.kind, family: /fail|refus|error/.test(entry.kind) ? 'bad' : /ask|gate/.test(entry.kind) ? 'needs' : undefined });
    });
  });

  $effect(() => {
    const picked = node;

    if (depth !== 0 || !picked) {
      return;
    }

    let live = true;
    stream = [];

    const read = () =>
      teamApi.stream(id, picked).then(
        (got) => live && (stream = got.steps),
        () => undefined,
      );

    void read();
    const timer = setInterval(read, 3000);

    return () => {
      live = false;
      clearInterval(timer);
    };
  });

  const streamLines = $derived<JournalLine[]>(
    stream.map((step) => ({
      time: clock(step.at),
      kind: step.kind,
      sub: step.sub,
      family: step.kind === 'refused' ? 'bad' : undefined,
      text: step.kind === 'tool' ? `${step.tool ?? ''}${step.target ? ` ${step.target}` : ''}` : (step.text ?? ''),
    })),
  );

  /* ---------- saying something to a node: the second credential ---------- */

  let attach = $state<{ set: boolean } | null>(null);
  let grant = $state<{ value: string; until: number } | null>(null);

  $effect(() => {
    if (node && !attach) {
      teamApi.attachState().then((state) => (attach = { set: state.set }), () => (attach = { set: false }));
    }
  });

  const nodeLive = $derived(!!(run?.running && run.nodes.find((one) => one.node === node && one.state === 'working')));
  const access = $derived<'unset' | 'locked' | 'open' | 'ended'>(!nodeLive ? 'ended' : !attach?.set ? 'unset' : grant && grant.until > Date.now() ? 'open' : 'locked');

  async function unlock(passphrase: string): Promise<boolean> {
    try {
      const got = await teamApi.attach(passphrase);

      if (got.grant) {
        grant = { value: got.grant, until: Date.now() + got.minutes * 60000 };

        return true;
      }
    } catch {
      // Refused: said by the form.
    }

    return false;
  }

  async function sayToNode(message: string): Promise<boolean> {
    if (!grant || !node) {
      return false;
    }

    const target = node;

    return studio.perform(() => teamApi.say(id, target, message, grant!.value), `Sent to ${target}`, `$ loadout team say ${id} --node ${target} --message …`, async () => undefined);
  }

  /* ---------- What it cost, The papers, What it changed, What it delivered ---------- */

  let papers = $state<{ node: string; role?: string; brief: string; report?: string }[] | null>(null);
  let summaryText = $state<string | null>(null);
  let diffs = $state<(NodeDiff | { node: string; error: string })[] | null>(null);
  interface OutboxJson {
    repository?: string;
    files: { path: string; change: 'added' | 'changed' | 'removed'; commit: string; node: string }[];
    loose?: { path: string; kind: string; bytes?: number; node: string; note?: string }[];
    missing?: string[];
  }

  let outbox = $state<(OutboxJson & { loose: NonNullable<OutboxJson['loose']>; missing: string[] }) | null>(null);

  $effect(() => {
    if (depth === 2 && !papers) {
      void loadPapers();
    } else if (depth === 3 && !diffs) {
      void loadDiffs();
    } else if (depth === 4 && !outbox) {
      readJson<OutboxJson>('team outbox', [id]).then((read) => (outbox = { ...read, loose: read.loose ?? [], missing: read.missing ?? [] }), (error: Error) => (problem = error.message));
    }
  });

  /** A paper's words: the brief's task, a report's summary, or the text itself where it is not JSON. */
  function paperOf(text: string): { node?: string; role?: string; words: string } {
    try {
      const read = JSON.parse(text) as { node?: string; role?: string; task?: string; summary?: string; status?: string };

      return { node: read.node, role: read.role, words: read.task ?? (read.summary ? `${read.status ? `${read.status}: ` : ''}${read.summary}` : text) };
    } catch {
      return { words: text };
    }
  }

  async function loadPapers() {
    try {
      const { documents } = await teamApi.documents(id);
      const briefs = documents.filter((one) => one.kind === 'brief');
      const pairs = [];

      for (const brief of briefs) {
        // brief-implementer-1-1.json answers to report-implementer-1-1.json.
        const report = documents.find((one) => one.kind === 'report' && one.name.replace(/^report-/, '') === brief.name.replace(/^brief-/, ''));
        const [told, said] = await Promise.all([teamApi.document(id, brief.name), report ? teamApi.document(id, report.name) : Promise.resolve(null)]);
        const paper = paperOf(told.text);

        pairs.push({ node: paper.node ?? brief.subject, role: paper.role, brief: paper.words, report: said ? paperOf(said.text).words : undefined });
      }

      const final = documents.find((one) => one.kind === 'final report');
      summaryText = final ? paperOf((await teamApi.document(id, final.name)).text).words : null;
      papers = pairs;
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  async function loadDiffs() {
    const branched = run?.nodes.filter((one) => one.branch) ?? [];

    diffs = await Promise.all(branched.map((one) => teamApi.diff(id, one.node).catch((error: Error) => ({ node: one.node, error: error.message }))));
  }

  const patchFiles = (patch: string): [string, number, number][] => {
    const files = new Map<string, [number, number]>();
    let current = '';

    for (const line of patch.replace(/\r/g, '').split('\n')) {
      const header = /^diff --git a\/(.+?) b\//.exec(line);

      if (header) {
        current = header[1];
        files.set(current, [0, 0]);
      } else if (current && line.startsWith('+') && !line.startsWith('+++')) {
        files.get(current)![0]++;
      } else if (current && line.startsWith('-') && !line.startsWith('---')) {
        files.get(current)![1]++;
      }
    }

    return [...files].map(([path, [plus, minus]]) => [path, plus, minus]);
  };

  /** A file's part of its node's patch, for The change. */
  async function changeOf(node: string, path: string): Promise<OutboxRead> {
    const diff = await teamApi.diff(id, node);
    const parts = diff.patch.replace(/\r/g, '').split(/^(?=diff --git )/m);
    const mine = parts.find((part) => part.startsWith(`diff --git a/${path} `));

    return { diff: mine ? diffLines(mine) : [] };
  }

  async function readFile(entry: { path: string; node: string; loose: boolean; commit?: string }, mode: 'file' | 'change'): Promise<OutboxRead> {
    if (mode === 'change' && !entry.loose) {
      return changeOf(entry.node, entry.path);
    }

    const address = teamApi.fileAddress(id, entry);
    const response = await fetch(address, { credentials: 'same-origin' });

    // 415: neither text nor an image; 422: text the redactor could not get
    // through in time, so it is not shown, though Download still works.
    if (response.status === 415) {
      return { binary: true };
    }

    if (response.status === 422) {
      return { unchecked: true };
    }

    if (!response.ok) {
      throw new Error((await response.text()) || `It answered ${response.status}.`);
    }

    const type = response.headers.get('Content-Type') ?? '';

    if (type.startsWith('image/')) {
      return { url: address };
    }

    const redacted = Number(response.headers.get('X-Loadout-Redacted-Lines') ?? 0) || undefined;
    // Cut at 2,000 lines, said so in a header.
    const truncated = response.headers.get('X-Loadout-Truncated') === 'true' ? 2000 : undefined;

    return { text: await response.text(), redacted, truncated };
  }

  function download(address: string) {
    const link = document.createElement('a');
    link.href = address;
    link.rel = 'noopener';
    link.click();
  }

  /* ---------- pull requests ---------- */

  async function openPr(target: string, o: { draft: boolean; title: string; dry: boolean }): Promise<string | null> {
    const words = [id, '--node', target, ...(o.draft ? ['--draft'] : []), ...(o.title ? [`--title=${o.title}`] : []), ...(o.dry ? ['--dry-run'] : [])];

    try {
      const job = await runJob('team pr', words);

      return `✓ ${(job.output ?? '').trim() || (o.dry ? 'Dry run: nothing was pushed.' : 'Opened.')}`;
    } catch (error) {
      return `✕ ${(error as Error).message}`;
    }
  }

  const watch = $derived(studio.watchOnly ? 'Studio is only watching: type the command in a terminal' : undefined);
</script>

{#if !run || !summary || !shape}
  <p class="calm" role="status">{studio.loaded ? 'That run is not on this machine, or it was forgotten.' : 'Reading…'} <a href="#teams">Back to Teams</a></p>
{:else}
  <div class="trun">
    <div class="trhead">
      <a class="back" href="#teams"><span aria-hidden="true">←</span>Teams</a>
      <h1 class="topicname">{run.goal}</h1>
      <div class="jobhead"><Signal word={summary.word} /><span class="calm small">{[run.team, run.project, run.autonomy, `round ${run.rounds}${run.roundLimit ? ` of ${run.roundLimit}` : ''}`, duration(run.elapsedSeconds), run.id].filter(Boolean).join(' · ')}</span>{#if badge}<span class="ls-liveness" role="status">{badge}</span>{/if}</div>
    </div>

    {#if gate}
      {#key gate.id}
        <Gate {...gateOf(run, gate)} run={run.id} disabledReason={watch} onanswer={(send) => answerGate(run.id, gate.id, send)} />
      {/key}
    {/if}

    {#each run.attention.filter((one) => one.kind !== 'asking') as one, index (index)}
      <p class="attention">{one.detail} <span class="calm small">· clears when {one.clears}</span></p>
    {/each}

    <div class="trcols">
      <div class="trmain">
        <section aria-labelledby="tr-shape">
          <h2 class="ls-zone" id="tr-shape">Its shape</h2>
          <RunShape rounds={shape.rounds} end={shape.end} picked={node} onpick={(picked) => { node = picked; depth = 0; }} />
        </section>
        <section aria-labelledby="tr-done">
          <h2 class="ls-zone" id="tr-done">Done when</h2>
          {#if outcomes}
            <DoneWhen {criteria} goal={run.goalUnderstood} by={run.coverage.some((one) => one.source === 'lead') ? 'proposed by the lead' : null} />
          {:else}
            <p class="calm" role="status">Reading…</p>
          {/if}
        </section>
        <section aria-labelledby="tr-crew">
          <h2 class="ls-zone" id="tr-crew">Who is on it</h2>
          <Crew nodes={crewOf(run)} picked={node} onpick={(picked) => { node = picked; depth = 0; }} />
          <p class="src">The same as <code class="ls-cmd">$ loadout team status {run.id}</code></p>
        </section>
      </div>

      <aside class="trside" aria-label="Steering this run">
        <section aria-labelledby="tr-spend">
          <h2 class="ls-zone" id="tr-spend">Spend</h2>
          <Spend spent={summary.spent ?? 0} cap={summary.cap} uncapped={summary.uncapped} projected={summary.projected} rate={summary.rate} />
          {#if run.running}
            <HaloButton label="Change what it may spend" size="small" cmd={runLines.budget(run.id, budget)} disabledReason={watch} onpress={() => (changing = true)} />
          {/if}
        </section>
        {#if run.running}
          <SayToNode run={run.id} lead disabledReason={watch} onsend={(message) => tellLead(run.id, message)} />
        {/if}
        <section aria-labelledby="tr-do">
          <h2 class="ls-zone" id="tr-do">Do</h2>
          <div class="ls-answers col">
            {#if unowned(run)}
              <HaloButton label="End run" kind="danger" size="small" cmd={runLines.close(run.id)} disabledReason={watch} onpress={() => endRun(run)} />
            {:else if run.running}
              <HaloButton label="Hold after this round" size="small" cmd={runLines.hold(run.id)} disabledReason={watch} onpress={() => void hold(run.id)} />
              <HaloButton label="Let it carry on" size="small" cmd={runLines.carryOn(run.id)} disabledReason={watch} onpress={() => void carryOn(run.id)} />
              <HaloButton label="Stop after this round" kind="danger" size="small" cmd={runLines.stop(run.id)} disabledReason={watch} onpress={() => void stop(run.id)} />
            {:else}
              <HaloButton label="Run it again" size="small" cmd="opens Start a run, filled from this run · starts nothing" onpress={() => onagain?.(run)} />
              <HaloButton label="Pick it up where it stopped" size="small" cmd={runLines.pickUp(run.id)} disabledReason={watch} onpress={() => void pickUp(run.id)} />
              <HaloButton label="Forget it" kind="danger" size="small" cmd={runLines.forget(run.id)} disabledReason={watch} onpress={() => (forgetting = true)} />
            {/if}
          </div>
        </section>
      </aside>
    </div>

    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    <RunDepths bind:selected={depth} keys={studio.keys}>
      {#snippet children(index)}
        {#if index === 0}
          <div class="who" role="group" aria-label="Whose words">
            <button type="button" aria-pressed={node ? 'false' : 'true'} onclick={() => (node = null)}>The run</button>
            {#each run.nodes as one (one.node)}
              <button type="button" aria-pressed={node === one.node ? 'true' : 'false'} onclick={() => (node = one.node)}>{one.node}</button>
            {/each}
          </div>
          {#if !node}
            <Journal lines={journal} />
            <p class="src">One line every few seconds, repeats counted. <code class="ls-cmd">$ loadout team log {run.id} --events</code></p>
          {:else}
            <Journal lines={streamLines.length ? streamLines : [{ time: '', text: `Nothing from ${node} yet.` }]} label={`What ${node} did`} />
            <p class="src">Everything {node} did: every tool it called and everything it said. Sub-agents are marked.</p>
            <SayToNode run={run.id} {node} {access} disabledReason={watch} minutesLeft={grant ? Math.max(1, Math.round((grant.until - Date.now()) / 60000)) : 30} onunlock={unlock} onsend={sayToNode} />
          {/if}
        {:else if index === 1}
          {#if run.numbers}
            <RunNumbers
              rate={run.numbers.money.rate}
              spent={run.numbers.money.spent}
              cap={run.numbers.money.budget}
              projected={run.numbers.money.projected}
              byRound={run.numbers.rounds.map((one): [string, number] => [`Round ${one.round}`, Math.round(one.seconds / 60)])}
              byNode={run.numbers.nodes.map((one): [string, number] => [one.node, Math.round(one.seconds / 60)])}
              wrong={[['Refused', run.numbers.trouble.denials], ['Reports sent back', run.numbers.trouble.rejected], ['Turns tried again', run.numbers.trouble.retried], ['Rounds with nothing asked', run.numbers.trouble.quietRounds], ['Merge conflicts', run.numbers.trouble.conflicts]]}
            />
          {/if}
          {#if run.turns?.length}
            <table class="ls-xch">
              <caption>Every exchange</caption>
              <thead><tr>{#each ['Round', 'Node', 'Model', 'Messages', 'Refused', 'Cost', 'Report'] as head, i (head)}<th scope="col" class={i >= 3 && i <= 5 ? 'r' : undefined}>{head}</th>{/each}</tr></thead>
              <tbody>
                {#each run.turns as turn, n (n)}
                  {@const model = run.nodes.find((one) => one.node === turn.node)?.model ?? 'not recorded'}
                  <tr>
                    {#each [turn.round, turn.node, model, turn.exchanges, turn.denials, money(turn.cost), turn.outcome ?? turn.status ?? ''] as cell, i (i)}
                      <td data-h={['Round', 'Node', 'Model', 'Messages', 'Refused', 'Cost', 'Report'][i]} class={i >= 3 && i <= 5 ? 'r' : i === 6 ? 'rep' : undefined}><span>{String(cell)}</span></td>
                    {/each}
                  </tr>
                {/each}
              </tbody>
            </table>
          {/if}
        {:else if index === 2}
          {#if !papers}
            <p class="calm" role="status">Reading…</p>
          {:else}
            <div class="ls-papers">
              {#if summaryText}<p class="sum">{summaryText}</p>{/if}
              {#if !papers.length}<p class="ls-note">No brief has gone out yet.</p>{/if}
              {#each papers as pair, n (n)}
                <section class="pair">
                  <h4>{pair.node}<span>{pair.role ?? ''}</span></h4>
                  <div class="two">
                    <div><h5>What it was told</h5><p>{pair.brief}</p></div>
                    <div><h5>What it said it did</h5><p>{pair.report ?? 'No report yet.'}</p></div>
                  </div>
                </section>
              {/each}
            </div>
          {/if}
        {:else if index === 3}
          {#if !diffs}
            <p class="calm" role="status">Reading…</p>
          {:else if !diffs.length}
            <p class="ls-note">No node of this run produced a patch.</p>
          {:else}
            {#each diffs as one (one.node)}
              {#if 'error' in one}
                <p class="ls-note">{one.node}: {one.error}</p>
              {:else}
                <Patch
                  run={run.id}
                  node={one.node}
                  branch={one.branch}
                  base={one.from}
                  goal={run.goal}
                  files={patchFiles(one.patch)}
                  diff={diffLines(one.patch)}
                  disabledReason={watch}
                  onpr={(o) => openPr(one.node, o)}
                />
              {/if}
            {/each}
          {/if}
        {:else if outbox}
          <Outbox
            run={run.id}
            repository={outbox.repository ?? null}
            files={outbox.files}
            loose={outbox.loose}
            missing={outbox.missing}
            read={readFile}
            ondownload={(entry) => download(teamApi.fileAddress(run.id, entry, true))}
            ondownloadall={() => download(teamApi.zipAddress(run.id))}
          />
        {:else}
          <p class="calm" role="status">Reading…</p>
        {/if}
      {/snippet}
    </RunDepths>
  </div>
{/if}

{#if changing && run}
  <Dialog
    title="Change what it may spend"
    why="The new total, not an addition. Say none to take the cap off on purpose."
    actions={[{ label: 'Change it', kind: 'needs', cmd: runLines.budget(run.id, budget.trim()), disabledReason: /^(none|\d+(\.\d+)?)$/i.test(budget.trim()) && !/^0+(\.0+)?$/.test(budget.trim()) ? undefined : 'a figure in US dollars, or none', onpress: () => void setBudget(run.id, budget.trim()) }]}
    onclose={() => (changing = false)}
  >
    <div class="ls-field">
      <label class="lab" for="run-budget">US dollars, or none</label>
      <input id="run-budget" name="run-budget" class="ls-text" type="text" autocomplete="off" placeholder={summary?.cap ? String(summary.cap) : 'none'} bind:value={budget} />
    </div>
  </Dialog>
{/if}

{#if forgetting && run}
  <Dialog
    danger
    title={`Forget ${run.id}?`}
    why={`“${run.goal}” goes to the bin, where it waits before it goes for good. Its branches are not touched.`}
    actions={[{ label: 'Forget it', kind: 'danger', cmd: runLines.forget(run.id), onpress: () => void forget(run.id).then((ok) => ok && router.go('teams')) }]}
    onclose={() => (forgetting = false)}
  />
{/if}

{#if ending && run}
  <Dialog
    danger
    title={`End ${run.id}?`}
    why="Nothing has written to its journal for a while and no process holds it, but it started before Loadout recorded who drives a run, so this cannot be sure it has gone. Ending it marks it abandoned and stops any node still shown as working."
    actions={[{ label: 'End run', kind: 'danger', cmd: `${runLines.close(run.id)} --yes`, onpress: () => void close(run.id, true).then(() => (ending = false)) }]}
    onclose={() => (ending = false)}
  />
{/if}
