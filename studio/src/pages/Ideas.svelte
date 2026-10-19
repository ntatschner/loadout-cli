<script lang="ts">
  // Ideas: a shelf of ideas beside a workbench for one. The workbench says
  // where the idea stands, what was understood and asked, the questions an
  // agent asked (answered here), the plan's layers side by side, and where
  // the plan goes when it is accepted. Every step is the command a terminal
  // would type, run on the daemon, so a round outlives the page that started
  // it. Notes kept elsewhere come in as dumps.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import IdeaStages from '../lib/components/IdeaStages.svelte';
  import NoteDump from '../lib/components/NoteDump.svelte';
  import PlanLayer from '../lib/components/PlanLayer.svelte';
  import QuestionSet from '../lib/components/QuestionSet.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { commands, memory, readJson, SignedOut } from '../lib/api';
  import { runJob } from '../lib/jobs';
  import { lineFor } from '../lib/commandLine';
  import { router } from '../lib/router.svelte';
  import { acceptWords, gaps, needsChoice, nextStop, sendAnswers, stateWord, stopTarget, stopWords, type DumpItem, type Question } from '../lib/ideas';
  import { studio } from '../lib/state.svelte';
  import { listHref, movedSaid, whereSaid } from '../lib/tasks';
  import { toasts } from '../lib/toasts.svelte';

  /** focused: just the workbench, for an idea opened on its own in Minimal. */
  let { rest, focused = false }: { rest: string[]; focused?: boolean } = $props();

  interface Listed {
    id: string;
    title: string;
    project?: string | null;
    stage: string;
    /** The idea's task state: open, doing, blocked, done or dropped. */
    state: string;
    unanswered: number;
    running: boolean;
    /** The last round, when it failed; absent once it is dismissed or a round succeeds. */
    failure?: { when: string | null; agent: string | null; model: string | null; error: string } | null;
    /** Where it was moved from, when it was. */
    moved?: { from: string | null; at: string } | null;
  }

  /** An idea that is a task on another list now, shown so it is not simply gone. */
  interface MovedOn {
    id: string;
    title: string;
    from: string | null;
    to: string | null;
    at: string;
  }

  interface Option {
    id: string;
    title: string;
    detail: string;
    pros: string[];
    cons: string[];
    recommended: boolean;
  }

  interface Layer {
    id: string;
    name: string;
    purpose: string;
    chosen: string | null;
    verdict: string;
    options: Option[];
  }

  interface Addition {
    id: string;
    title: string;
    why: string;
    verdict: string;
  }

  interface Idea {
    id: string;
    project: string | null;
    stage: string;
    ask: string;
    questions: Question[];
    plan: {
      revision: number;
      title: string;
      understanding: string;
      layers: Layer[];
      additions: Addition[];
      placement: { slug: string; isNew: boolean; name: string; reason: string } | null;
    } | null;
    accepted: { project: string; plan: string; by: string; when: string } | null;
  }

  interface Dump {
    id: string;
    source: string;
    captured: string;
    split: string | null;
    items: DumpItem[];
  }

  let ideas = $state<Listed[] | null>(null);
  let movedOn = $state<MovedOn[]>([]);
  let idea = $state<Idea | null>(null);
  let dumps = $state<Dump[] | null>(null);
  let projects = $state<{ slug: string; name: string }[]>([]);
  let problem = $state<string | null>(null);
  let showing = $state<{ id: string; text: string } | null>(null);
  // What is typed on the workbench and not sent, as the parts holding it report it.
  let drafted = $state<string[]>([]);
  let sentAnswers = $state<string[]>([]);
  let reworks = $state<Record<string, boolean>>({});
  let work = $state<HTMLElement | null>(null);

  // #ideas/<list>/<id>, where the list is a project's slug or '~' for the
  // workspace-wide one; #ideas/+add and #ideas/+dumps for the rest. With
  // none named, the first idea on the shelf is on the workbench.
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);
  const first = $derived(ideas?.[0] ?? null);
  const list = $derived(place ? null : (rest[0] ?? (first ? (first.project ?? '~') : null)));
  const id = $derived(place ? null : (rest[1] ?? (rest[0] ? null : (first?.id ?? null))));
  const project = $derived(list && list !== '~' ? list : null);
  const where = $derived(project ? ['--project', project] : ['--global']);
  const listed = $derived(ideas?.find((one) => one.id === id && (one.project ?? null) === project) ?? null);
  const word = $derived(listed ? stateWord(listed) : idea ? stateWord({ stage: idea.stage, unanswered: idea.questions.filter((one) => !one.answer).length }) : '');
  const refused = $derived(studio.watchOnly ? studio.watchOnlyReason : undefined);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadList() {
    try {
      const read = await readJson<{ ideas: Listed[]; movedOn?: MovedOn[] }>('idea list');
      ideas = read.ideas;
      movedOn = read.movedOn ?? [];
    } catch (error) {
      fail(error);
    }
  }

  async function loadIdea(name: string, words: string[]) {
    try {
      idea = await readJson<Idea>('idea show', [name, ...words]);
    } catch (error) {
      fail(error);
    }
  }

  function loadDumps() {
    readJson<{ dumps: Dump[] }>('idea dump list').then((read) => (dumps = read.dumps), fail);
  }

  $effect(() => {
    void loadList();
    memory.projects().then((all) => (projects = all), () => (projects = []));
  });

  $effect(() => {
    idea = null;
    problem = null;
    untrack(() => {
      drafted = [];
      sentAnswers = [];
      reworks = {};
    });

    if (id) {
      void loadIdea(id, where);
    }
  });

  $effect(() => {
    if (place === 'dumps') {
      loadDumps();
    }
  });

  // A job that ended may have moved an idea on, so it is read again in place.
  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;

    untrack(() => {
      void loadList();

      if (id) {
        void loadIdea(id, where);
      }

      if (place === 'dumps') {
        loadDumps();
      }
    });
  });

  /** Runs one step as a job; what happened comes back as a toast, and the page reads again when it ends. */
  async function act(path: string, words: string[]): Promise<boolean> {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor(path, words) });

      return false;
    }

    try {
      await commands.run(path, words);
      await studio.loadJobs();

      return true;
    } catch (error) {
      fail(error);

      return false;
    }
  }

  /**
   * Runs one step and waits for it to end, true only if it succeeded: how
   * each answer is sent, so the next one, and the refine after them, start
   * only once it is recorded. Started without waiting, a refine ran before
   * its answers were in and was refused for the questions they answered.
   */
  async function actToEnd(path: string, words: string[]): Promise<boolean> {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor(path, words) });

      return false;
    }

    try {
      await runJob(path, words);
      await studio.loadJobs();

      return true;
    } catch (error) {
      fail(error);

      return false;
    }
  }

  async function show(dump: string) {
    try {
      const read = await commands.read('idea dump show', [dump]);

      showing = { id: dump, text: (read.output ?? read.error ?? '').trim() };
    } catch (error) {
      fail(error);
    }
  }

  const groups = $derived.by(() => {
    const by = new Map<string, Listed[]>();

    for (const one of ideas ?? []) {
      const key = one.project ?? '~';
      by.set(key, [...(by.get(key) ?? []), one]);
    }

    return [...by.entries()];
  });

  const href = (one: Listed) => `#ideas/${encodeURIComponent(one.project ?? '~')}/${encodeURIComponent(one.id)}`;
  const listFixed = $derived<Record<string, string | boolean>>(project ? { '--project': project } : { '--global': true });
  const waiting = $derived((ideas ?? []).filter((one) => stateWord(one) === 'waiting for you').length);
  const shaping = $derived((ideas ?? []).filter((one) => one.stage !== 'accepted').length);

  // Accept it: onto a project, or as a new one. Starts where the plan says it goes.
  let goes = $state<'project' | 'new'>('project');
  let onto = $state('');
  let named = $state('');

  $effect(() => {
    const placement = idea?.plan?.placement ?? null;

    untrack(() => {
      goes = placement?.isNew ? 'new' : 'project';
      onto = placement && !placement.isNew ? placement.slug : (project ?? projects[0]?.slug ?? '');
      named = placement?.isNew ? placement.name : '';
    });
  });

  const undecided = $derived(idea?.plan?.layers.filter(needsChoice).length ?? 0);
  const acceptLine = $derived(idea ? acceptWords(idea.id, where, goes === 'project' ? { project: onto || '<slug>' } : { newProject: named.trim() || '<name>' }) : []);
  const acceptReason = $derived(refused ?? (goes === 'project' ? (onto ? undefined : 'choose the project it goes on') : named.trim() ? undefined : 'name the new project'));

  // Next: the next thing on the open idea that needs you, then the button that saves what is typed.
  const drafts = $derived({ answers: drafted, sent: sentAnswers, reworks: Object.keys(reworks).filter((piece) => reworks[piece]) });
  // Watching only, nothing typed can be sent, so the send stops are passed over.
  const stop = $derived(idea ? nextStop(idea, refused ? { ...drafts, answers: [], reworks: [] } : drafts) : null);
  const left = $derived(idea ? gaps(idea, drafts).length : 0);
  const unsent = $derived(drafts.answers.length + drafts.reworks.length > 0);

  function goNext() {
    const target = stop && work?.querySelector<HTMLElement>(`[data-next="${stopTarget(stop)}"]`);

    if (!target) {
      return;
    }

    const still = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    const focusable = target.matches('button, input, textarea, select') ? target : target.querySelector<HTMLElement>('input, textarea, select, button');

    target.scrollIntoView?.({ block: 'center', behavior: still ? 'auto' : 'smooth' });
    focusable?.focus({ preventScroll: true });

    if (!still) {
      target.classList.remove('next-here');
      void target.offsetWidth;
      target.classList.add('next-here');
      setTimeout(() => target.classList.remove('next-here'), 1100);
    }
  }

  // ] goes to the next stop, outside text fields: N is taken, and typing is when single keys are ignored.
  function nextKey(event: KeyboardEvent) {
    const typing = (event.target as HTMLElement | null)?.closest('input, textarea, select, [contenteditable="true"], .xterm');

    if (event.key !== ']' || !stop || typing || event.ctrlKey || event.metaKey || event.altKey || !studio.keys || document.querySelector('.ls-dialog')) {
      return;
    }

    event.preventDefault();
    goNext();
  }

  // Leaving with answers or a rework typed and not sent loses them, so the browser asks first.
  function leaving(event: BeforeUnloadEvent) {
    if (unsent) {
      event.preventDefault();
    }
  }

  // Drop an idea in.
  let text = $state('');
  let forList = $state('~');
  const addWords = $derived([`--text=${text.trim() || '<idea>'}`, ...(forList === '~' ? ['--global'] : ['--project', forList])]);

  async function drop() {
    if (await act('idea add', addWords)) {
      toasts.add({ level: 'done', text: 'Dropped it in. It is on the shelf; refine it when you want an agent to flesh it out.', cmd: lineFor('idea add', addWords) });
      text = '';
    }
  }
</script>

<svelte:window onkeydown={nextKey} onbeforeunload={leaving} />

{#if !focused}
<div class="head">
  <div><h1>Ideas</h1><span class={['fact', waiting ? 'need' : ''].filter(Boolean).join(' ')}>{ideas ? `${shaping} being shaped${waiting ? ` · ${waiting} waiting on you` : ''}` : 'ideas you are shaping, a round at a time'}</span></div>
  <span class="grow"></span>
  <HaloButton label="Drop an idea in" size="small" solid glyph="+" cmd="opens the box · records nothing yet" onpress={() => router.go('ideas', '+add')} />
</div>
{/if}

<div class={['bench', focused ? 'solo' : ''].filter(Boolean).join(' ')}>
  {#if !focused}
  <nav class="shelf" aria-label="Ideas">
    {#if ideas === null}
      <p class="calm" role="status">Reading…</p>
    {:else if ideas.length === 0}
      <p class="calm">No ideas being shaped. Drop one in.</p>
    {/if}

    {#each groups as [key, ones] (key)}
      <h2 class="gh">{key === '~' ? 'Workspace-wide' : key}</h2>
      <ul>
        {#each ones as one (one.id)}
          <li>
            <a href={href(one)} aria-current={!place && id === one.id && (one.project ?? null) === project ? 'page' : undefined}>
              <span class="t">{one.title}</span>
              <Signal word={stateWord(one)} />
              <span class="x">{one.failure ? 'last round failed' : one.unanswered ? `${one.unanswered} question${one.unanswered === 1 ? '' : 's'} for you` : one.stage}</span>
            </a>
          </li>
        {/each}
      </ul>
    {/each}

    {#if movedOn.length}
      <h2 class="gh">Became tasks elsewhere</h2>
      <ul class="gone">
        {#each movedOn as gone (gone.id)}
          <li>
            <a class="place" href={listHref(gone.to)}>
              <span class="t">{gone.title || gone.id}</span>
              <span class="x">was on {whereSaid(gone.from)} · a task on {whereSaid(gone.to)} now</span>
            </a>
          </li>
        {/each}
      </ul>
    {/if}

    <h2 class="gh">More</h2>
    <ul>
      <li><a class="place" href="#ideas/+add" aria-current={place === 'add' ? 'page' : undefined}><span class="t">Drop an idea in</span></a></li>
      <li><a class="place" href="#ideas/+dumps" aria-current={place === 'dumps' ? 'page' : undefined}><span class="t">Notes kept elsewhere</span></a></li>
    </ul>
    <p class="ls-from">From <code>$ loadout idea list</code>.</p>
  </nav>
  {/if}

  <section class={['work', stop ? 'has-next' : ''].filter(Boolean).join(' ')} aria-label="Idea" bind:this={work}>
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if id && idea}
      <div class="ihead">
        <h2>{idea.plan?.title ?? idea.ask}</h2>
        <p class="slug">{idea.id} · {project ?? 'workspace-wide'}{idea.plan ? ` · plan revision ${idea.plan.revision}` : ''}</p>
        {#if listed?.moved}<p class="slug">Moved here {movedSaid(listed.moved)}</p>{/if}
      </div>
      <IdeaStages stage={idea.stage} {word} />
      {#if idea.plan?.understanding}<p class="understand">{idea.plan.understanding}</p>{/if}
      <details class="asked"><summary>What was asked</summary><p>{idea.ask}</p></details>

      {#if idea.accepted}
        <p class="accepted">Accepted onto <strong>{idea.accepted.project}</strong> by {idea.accepted.by}. The plan is at <code>{idea.accepted.plan}</code>.</p>
      {/if}

      {#if listed?.failure}
        <div class="waitcard problem" role="alert">
          <span class="w">last round failed{listed.failure.agent ? ` · ${listed.failure.agent}${listed.failure.model ? `, model ${listed.failure.model}` : ''}` : ''}{listed.failure.when ? ` · ${new Date(listed.failure.when).toLocaleString()}` : ''}</span>
          <p class="q">{listed.failure.error}</p>
          <div class="ls-answers flush">
            <HaloButton label="Try the round again" size="small" glyph="↻" cmd={lineFor('idea refine', [idea.id, ...where])} disabledReason={refused} onpress={() => void act('idea refine', [idea!.id, ...where])} />
            <HaloButton label="Clear the marker" size="small" cmd={lineFor('idea dismiss', [idea.id, ...where])} disabledReason={refused} onpress={() => void act('idea dismiss', [idea!.id, ...where])} />
          </div>
        </div>
      {/if}

      {#if idea.questions.length && !idea.accepted}
        <section class="pblock" aria-labelledby="iq">
          <h2 class="ls-zone" id="iq">Questions for you</h2>
          <QuestionSet id={idea.id} {where} questions={idea.questions} disabledReason={refused} ondraft={(answers, sent) => ((drafted = answers), (sentAnswers = sent))} onsend={(answers, refine) => sendAnswers(idea!.id, where, answers, refine, (path, words) => (path === 'idea answer' ? actToEnd(path, words) : act(path, words)))} />
        </section>
      {/if}

      {#if idea.plan && (idea.plan.layers.length || idea.plan.additions.length)}
        <section class="pblock" aria-labelledby="ip">
          <h2 class="ls-zone" id="ip">The plan · revision {idea.plan.revision}</h2>
          {#each idea.plan.layers as layer (layer.id)}
            <PlanLayer idea={idea.id} {where} {layer} accepted={!!idea.accepted} disabledReason={refused}
              onchoose={(option) => void act('idea choose', [idea!.id, layer.id, option, ...where])}
              onverdict={(verdict) => void act(verdict === 'Keep' ? 'idea keep' : 'idea drop', [idea!.id, layer.id, ...where])}
              onimprove={(request) => void act('idea improve', [idea!.id, layer.id, `--request=${request}`, ...where])}
              onrework={(pending) => untrack(() => (reworks = { ...reworks, [layer.id]: pending }))} />
          {/each}
          {#if idea.plan.additions.length}
            <h3 class="sub">Additions the agent proposes</h3>
            {#each idea.plan.additions as addition (addition.id)}
              <PlanLayer idea={idea.id} {where} {addition} accepted={!!idea.accepted} disabledReason={refused}
                onverdict={(verdict) => void act(verdict === 'Keep' ? 'idea keep' : 'idea drop', [idea!.id, addition.id, ...where])}
                onimprove={(request) => void act('idea improve', [idea!.id, addition.id, `--request=${request}`, ...where])}
                onrework={(pending) => untrack(() => (reworks = { ...reworks, [addition.id]: pending }))} />
            {/each}
          {/if}
        </section>
      {/if}

      {#if idea.plan && !idea.accepted}
        <section class="accept" aria-labelledby="ia">
          <h2 class="ls-zone" id="ia">Accept it</h2>
          {#if idea.plan.placement}
            <p class="place">The agent would put it on <strong>{idea.plan.placement.name}</strong>{idea.plan.placement.isNew ? ', a new project' : ''}: {idea.plan.placement.reason}</p>
          {/if}
          <fieldset>
            <legend>Where the plan goes</legend>
            <label class="opt">
              <input type="radio" name="accept-goes" value="project" bind:group={goes} />
              Onto a project
              <select class="ls-text" name="accept-onto" aria-label="The project it goes on" bind:value={onto} disabled={goes !== 'project'}>
                {#each projects as one (one.slug)}<option value={one.slug}>{one.name} · {one.slug}</option>{/each}
              </select>
            </label>
            <label class="opt">
              <input type="radio" name="accept-goes" value="new" bind:group={goes} />
              As a new project
              <input class="ls-text" name="accept-new-project" aria-label="The new project's name" placeholder="its name" bind:value={named} disabled={goes !== 'new'} />
            </label>
          </fieldset>
          {#if undecided}<p><span class="ls-tag needs">{undecided} layer{undecided === 1 ? ' has' : 's have'} no option chosen</span> It can still be accepted; the plan says so where it is written.</p>{/if}
          <div class="ls-answers flush">
            <HaloButton label="Accept the plan" next="accept" solid={!acceptReason} glyph="✓" size="small" cmd={lineFor('idea accept', acceptLine)} disabledReason={acceptReason} onpress={() => void act('idea accept', acceptLine)} />
          </div>
        </section>
      {/if}

      {#if !idea.accepted}
        <section class="pblock" aria-labelledby="im">
          <h2 class="ls-zone" id="im">More</h2>
          <div class="ls-drawers">
            <CommandAction path="idea refine" title="Take it one round further" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Refine" open={!idea.questions.length && !idea.plan && !listed?.failure} />
            <CommandAction path="idea improve" title="Ask for the whole plan to be reworked" preset={{ id: idea.id, piece: 'plan', ...listFixed }} fixed={['id', 'piece', '--project', '--global']} label="Ask for it" />
            <CommandAction path="idea move" title="Move it to another project" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Move it" note="Its answers, choices and plan go with it." />
            <CommandAction path="idea remove" title="Forget this idea" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Forget it" danger />
          </div>
          <p class="ls-from">A round asks an agent and takes a minute or two. It runs on the daemon, so you can leave this page; a toast says when it has finished.</p>
        </section>
      {/if}

      <Command why="The same, in a terminal:" command={lineFor('idea show', [idea.id, ...where])} />

      {#if stop}
        <div class="ls-next">
          {#if left}<span class="left" aria-hidden="true">{left}</span>{/if}
          <HaloButton label={`Next: ${stopWords(stop)}`} size="small" solid glyph="↓" describedBy="ls-next-left"
            cmd={`changes nothing · ]${left ? ` · ${left} left to decide` : ''}`}
            disabledReason={refused && stop.kind === 'accept' ? refused : undefined} onpress={goNext} />
          <span class="ls-sr" id="ls-next-left">{left ? `${left} left to decide on this idea` : 'nothing left to decide on this idea'}</span>
        </div>
      {/if}
    {:else if id}
      <p class="calm" role="status">Reading…</p>
    {:else if place === 'add'}
      <div class="ihead"><h2>Drop an idea in</h2><p class="slug">an agent fleshes it out when you refine it</p></div>
      <label class="ls-vh" for="idea-text">The idea, in your own words</label>
      <textarea id="idea-text" class="ls-text big" rows="5" placeholder="The idea, in your own words. It is kept as you write it." bind:value={text}></textarea>
      <div class="accept wherebar">
        <label class="opt"><span class="lab">For</span>
          <select class="ls-text" name="idea-for" bind:value={forList}>
            <option value="~">No project yet</option>
            {#each projects as one (one.slug)}<option value={one.slug}>{one.name} · {one.slug}</option>{/each}
          </select>
        </label>
      </div>
      <div class="ls-answers flush">
        <HaloButton label="Drop it in" solid={!refused && !!text.trim()} glyph="+" cmd={lineFor('idea add', addWords)} disabledReason={refused ?? (text.trim() ? undefined : 'write the idea first')} onpress={() => void drop()} />
      </div>
    {:else if place === 'dumps'}
      <div class="ihead"><h2>Notes kept elsewhere</h2><p class="slug">split into ideas and tasks by an agent; nothing is recorded until you say</p></div>
      <CommandAction path="idea dump add" title="Drop notes in" open />
      {#if dumps === null}
        <p class="calm" role="status">Reading…</p>
      {:else}
        {#each dumps as dump (dump.id)}
          <NoteDump {dump} disabledReason={refused} onapply={(words) => void act('idea dump apply', words)} onsplit={() => void act('idea dump split', [dump.id])} onshow={() => void show(dump.id)} />
        {:else}
          <p class="calm">No notes dropped in yet.</p>
        {/each}
        <p class="ls-from">From <code>$ loadout idea dump list</code>.</p>
      {/if}
    {:else if ideas?.length === 0}
      <p class="calm">Nothing on the shelf yet. Drop an idea in, and refine it when you want an agent to flesh it out.</p>
    {/if}
  </section>
</div>

{#if showing}
  <Dialog title={`Notes ${showing.id}`} why={lineFor('idea dump show', [showing.id])} cancelLabel="Close" onclose={() => (showing = null)}>
    <pre class="dumpshow">{showing.text}</pre>
  </Dialog>
{/if}
