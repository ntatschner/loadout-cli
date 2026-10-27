<script lang="ts">
  // Ideas: a shelf of ideas beside a workbench for one. The workbench says
  // where the idea stands, what was understood and asked, the questions an
  // agent asked (answered here), the plan's layers side by side, and where
  // the plan goes when it is accepted. Every step is the command a terminal
  // would type, run on the daemon, so a round outlives the page that started
  // it. Notes kept elsewhere come in as dumps.
  //
  // When a step ends the idea is read again, and only what changed is drawn
  // again; what you are looking at stays where it is on screen. A round that
  // brings new questions or a new revision of the plan takes you to them,
  // unless you have scrolled or typed since it started: then it offers to.
  import { tick, untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import IdeaStages from '../lib/components/IdeaStages.svelte';
  import NoteDump from '../lib/components/NoteDump.svelte';
  import PlanLayer from '../lib/components/PlanLayer.svelte';
  import QuestionSet from '../lib/components/QuestionSet.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { commands, ideas as ideaRoutes, listOf, memory, readJson, SignedOut } from '../lib/api';
  import ListToolbar from '../lib/components/ListToolbar.svelte';
  import { CommandList } from '../lib/commandList.svelte';
  import { lineFor } from '../lib/commandLine';
  import { holdPlace } from '../lib/holdPlace';
  import { runJob } from '../lib/jobs';
  import { router } from '../lib/router.svelte';
  import { acceptWords, doneWhenLines, doneWhenWords, arrivalIn, gaps, keepUnchanged, needsChoice, nextStop, sendAnswers, stateWord, stopTarget, stopWords, type Arrival, type DumpItem, type Question, type Round } from '../lib/ideas';
  import { studio } from '../lib/state.svelte';
  import { ideaHref, listHref, movedSaid, whereSaid } from '../lib/tasks';
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
    rounds?: Round[];
    outdated?: number;
    plan: {
      revision: number;
      title: string;
      understanding: string;
      layers: Layer[];
      additions: Addition[];
      placement: { slug: string; isNew: boolean; name: string; reason: string } | null;
      doneWhen?: string[];
    } | null;
    accepted: { project: string; plan: string; by: string; when: string } | null;
    /** What was added since it was asked; taken is when a round read it, null until one has. */
    notes?: { id: string; text: string; by: string; added: string; taken: string | null }[];
  }

  interface Dump {
    id: string;
    source: string;
    captured: string;
    split: string | null;
    items: DumpItem[];
  }

  // The shelf, through 'idea list' with the shared query. Read whole up to a
  // limit no workspace is near, rather than a page at a time, because the
  // counts above it and each idea's failure card are worked out from what it
  // holds; past the limit it says so and the search box narrows it.
  const SHELF_LIMIT = 200;
  const shelf = new CommandList<Listed>('idea list', 'ideas', 'ideas');
  const ideas = $derived(shelf.items);
  const beyond = $derived(((shelf.read?.total as number | undefined) ?? 0) - (ideas?.length ?? 0));
  const movedOn = $derived((shelf.read?.movedOn as MovedOn[] | undefined) ?? []);
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
  // Something a round brought while you were busy elsewhere on the page, offered rather than gone to.
  let arrived = $state<Arrival | null>(null);
  // When a round was last seen starting, and when you last scrolled or typed: a round that ends after you have moved does not move you.
  let roundAt = 0;
  let stirredAt = 0;

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
    await untrack(() => shelf.load(['--limit', String(SHELF_LIMIT)]));
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

  /**
   * Reads the open idea again, with the shelf when asked, and draws only what
   * changed. What you are looking at is held in place, unless the read
   * brought new questions or a new revision and you have not moved since the
   * round started: then the page goes to the top of them.
   */
  async function reread(withShelf = false) {
    const name = id;
    const words = where;
    const held = work ? holdPlace(work) : null;
    let fresh: Idea | null = null;

    try {
      [fresh] = await Promise.all([name ? readJson<Idea>('idea show', [name, ...words]) : Promise.resolve(null), withShelf ? loadList() : Promise.resolve()]);
    } catch (error) {
      fail(error);
    }

    if (name !== id || words.join(' ') !== where.join(' ')) {
      return;
    }

    const was = idea;
    const news = fresh && was && was.id === fresh.id ? arrivalIn(was, fresh) : null;

    if (fresh) {
      idea = was && was.id === fresh.id ? keepUnchanged(was, fresh) : fresh;
    }

    await tick();

    if (news && stirredAt <= roundAt) {
      arrived = null;
      goTo(news);
    } else {
      held?.();

      if (news) {
        arrived = news;
      }
    }
  }

  // The top of what a round brought, scrolled to and focused, so a keyboard or a screen reader lands there too.
  function goTo(news: Arrival) {
    const heading = work?.querySelector<HTMLElement>(news === 'questions' ? '#iq' : '#ip') ?? work?.querySelector<HTMLElement>('.ihead h2');

    if (!heading) {
      return;
    }

    const still = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

    arrived = null;
    heading.scrollIntoView?.({ block: 'start', behavior: still ? 'auto' : 'smooth' });
    heading.focus({ preventScroll: true });
  }

  // Scrolling by hand or typing: what a round that ends afterwards must not take away from you.
  function stirred() {
    stirredAt = Date.now();
  }

  // A round seen running on the shelf counts as started then, wherever it was
  // started from. A boolean, because the shelf is rebuilt on every read.
  const running = $derived(!!listed?.running);

  $effect(() => {
    if (running) {
      untrack(() => (roundAt = Date.now()));
    }
  });

  $effect(() => {
    idea = null;
    problem = null;
    untrack(() => {
      more = '';
      refineNow = true;
      drafted = [];
      sentAnswers = [];
      reworks = {};
      arrived = null;
      roundAt = Date.now();
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
      void reread(true);

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

    if (path === 'idea refine') {
      roundAt = Date.now();
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
   * Runs one quick change to a piece of the plan and waits for it, then reads
   * the idea again: true once the change is on the page. When it fails the
   * piece goes back to what it was, and a toast says so and why.
   */
  async function record(path: string, words: string[], undone: string): Promise<boolean> {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor(path, words) });

      return false;
    }

    try {
      await runJob(path, words);
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: `${undone} ${(error as Error).message}`, cmd: lineFor(path, words) });
      }

      return false;
    } finally {
      void studio.loadJobs().catch(() => undefined);
    }

    await reread();

    return true;
  }

  /** What a piece goes back to when a change to it was not recorded, in words. */
  function chooseUndone(layer: Layer, option: string): string {
    return `Choosing ${option} for ${layer.id} was not recorded, so ${layer.chosen ? `${layer.chosen} is chosen again` : 'nothing is chosen'}.`;
  }

  function verdictUndone(piece: Layer | Addition, to: string): string {
    return `${to} for ${piece.id} was not recorded, so its verdict is ${String(piece.verdict || 'undecided').toLowerCase()} again.`;
  }

  /**
   * Sends several changes to one idea as one batch, made together or not at
   * all, and says the reason when one was refused rather than an exit code.
   */
  async function batch(id: string, where: string[], operations: Record<string, string>[]): Promise<boolean> {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: `loadout idea apply - ${where.join(' ')}` });

      return false;
    }

    try {
      const done = await ideaRoutes.batch(listOf(where), operations);

      if (!done.applied) {
        const refused = done.results.find((one) => !one.applied && !one.heldBack);

        toasts.add({ level: 'problem', text: `None of them were recorded. ${refused?.error ?? 'One was refused.'}` });
      }

      await reread();

      return done.applied;
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

  // Dropped ideas fold away under the groups, as done and dropped tasks do
  // on a board: kept, and out of the way.
  const dropped = $derived((ideas ?? []).filter((one) => one.state === 'dropped'));

  const groups = $derived.by(() => {
    const by = new Map<string, Listed[]>();

    for (const one of (ideas ?? []).filter((idea) => idea.state !== 'dropped')) {
      const key = one.project ?? '~';
      by.set(key, [...(by.get(key) ?? []), one]);
    }

    return [...by.entries()];
  });

  const href = (one: Listed) => ideaHref(one.project, one.id);
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

  // Done when: what the plan proposed, to confirm or rewrite before accepting.
  // Filled again only when a different idea or a new revision of its plan
  // arrives, never on a poll that brings the same plan back, or what somebody
  // is typing would be wiped under them.
  let doneWhenText = $state('');
  let doneWhenFor = '';

  $effect(() => {
    const key = idea?.plan ? `${idea.id}:${idea.plan.revision}` : '';
    const proposed = idea?.plan?.doneWhen ?? [];

    untrack(() => {
      if (key !== doneWhenFor) {
        doneWhenFor = key;
        doneWhenText = proposed.join(String.fromCharCode(10));
      }
    });
  });

  const proposedDoneWhen = $derived(idea?.plan?.doneWhen ?? []);
  const doneWhenCount = $derived(doneWhenLines(doneWhenText).length);

  const undecided = $derived(idea?.plan?.layers.filter(needsChoice).length ?? 0);
  const acceptLine = $derived(idea ? acceptWords(idea.id, where, goes === 'project' ? { project: onto || '<slug>' } : { newProject: named.trim() || '<name>' }, doneWhenWords(doneWhenText, proposedDoneWhen)) : []);
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

  // Add to it: a note for the next round, with that round started now unless
  // the box is unticked. Kept only here, so a poll that reads the idea again
  // leaves what is typed alone; emptied when a different idea is opened.
  let more = $state('');
  let refineNow = $state(true);
  let adding = $state(false);
  const open = $derived(idea ? idea.questions.filter((one) => !one.answer).length : 0);
  const addToWords = $derived(idea ? ['--to', idea.id, `--text=${more.trim() || '<note>'}`, ...(refineNow ? ['--refine'] : []), ...where] : []);

  /**
   * Sends the note as one action; the daemon starts the round when asked to.
   * A refusal comes back in the daemon's words, which say whether the note
   * was saved, so a failed round never reads as a failed add.
   */
  async function addTo() {
    if (!idea || adding) {
      return;
    }

    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor('idea add', addToWords) });

      return;
    }

    const asked = idea.id;
    const cmd = lineFor('idea add', addToWords);

    adding = true;

    try {
      const reply = await ideaRoutes.act({ verb: 'addto', id: asked, project, text: more, ...(refineNow ? { refine: true } : {}) });
      // The daemon says so when no round started, as while questions are open.
      const said = (reply as unknown as { said?: string | null } | undefined)?.said;
      toasts.add({
        level: 'done',
        text: said
          ? said
          : refineNow
          ? `Added to ${asked}, and a round has started on it. It usually takes a minute or two.`
          : `Added to ${asked}. No round was started; the next one reads it.`,
        cmd,
      });
      more = '';
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd });
      }
    } finally {
      adding = false;
    }

    await reread(true);
  }

  /** Takes off a note no round has read; the daemon refuses one that has been. */
  async function removeNote(note: string) {
    if (!idea) {
      return;
    }

    const cmd = lineFor('idea note remove', [idea.id, note, ...where]);

    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd });

      return;
    }

    try {
      await ideaRoutes.act({ verb: 'note-remove', id: idea.id, project, note });
      toasts.add({ level: 'done', text: `${note} is removed. Its number is not given to another note.`, cmd });
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd });
      }
    }

    await reread();
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

<svelte:window onkeydown={(event) => (stirred(), nextKey(event))} onwheel={stirred} ontouchmove={stirred} onbeforeunload={leaving} />

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
    <ListToolbar noun="ideas" query={shelf.text} shape={shelf.shape} shown={ideas?.length ?? null} recorded={shelf.recorded} problem={shelf.problem} onchange={(query) => void shelf.ask(query)} />
    {#if shelf.error}
      <p class="offline" role="alert">{shelf.error}</p>
    {:else if ideas === null}
      <p class="calm" role="status">Reading…</p>
    {:else if ideas.length === 0 && !shelf.recorded}
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

    {#if dropped.length}
      <details class="dropped">
        <summary>{dropped.length} dropped</summary>
        <ul>
          {#each dropped as one (`${one.project ?? '~'}/${one.id}`)}
            <li>
              <a href={href(one)} aria-current={!place && id === one.id && (one.project ?? null) === project ? 'page' : undefined}>
                <span class="t">{one.title}</span>
                <span class="x">{whereSaid(one.project)} · dropped</span>
              </a>
            </li>
          {/each}
        </ul>
      </details>
    {/if}

    {#if beyond > 0}
      <p class="calm">The newest {ideas?.length} are shown and {beyond} more are not. Narrow the shelf with the search box, or see them all with <code>loadout idea list</code>.</p>
    {/if}

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
      <div class="ihead" data-anchor="head">
        <h2 tabindex="-1">{idea.plan?.title ?? idea.ask}</h2>
        <p class="slug">{idea.id} · {project ?? 'workspace-wide'}{idea.plan ? ` · plan revision ${idea.plan.revision}` : ''}</p>
        {#if listed?.moved}<p class="slug">Moved here {movedSaid(listed.moved)}</p>{/if}
      </div>
      <IdeaStages stage={idea.stage} {word} />
      {#if idea.plan?.understanding}<p class="understand">{idea.plan.understanding}</p>{/if}
      <details class="asked"><summary>What was asked</summary><p>{idea.ask}</p></details>

      {#if idea.accepted}
        <p class="accepted">Accepted onto <strong>{idea.accepted.project}</strong> by {idea.accepted.by}. The plan is at <code>{idea.accepted.plan}</code>. <a href={listHref(idea.accepted.project)}>Its task is on {whereSaid(idea.accepted.project)}'s board</a>.</p>
      {/if}

      {#if listed?.failure}
        <div class="waitcard problem" role="alert" data-anchor="failure">
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
          <h2 class="ls-zone" id="iq" tabindex="-1" data-anchor="zone:questions">Questions for you{#if idea.outdated}<span class="outdated-count">{` · ${idea.outdated} possibly outdated`}</span>{/if}</h2>
          <QuestionSet id={idea.id} {where} questions={idea.questions} rounds={idea.rounds} disabledReason={refused} ondraft={(answers, sent) => ((drafted = answers), (sentAnswers = sent))} onsend={(answers, refine) => sendAnswers(idea!.id, where, answers, refine, (operations) => batch(idea!.id, where, operations), act)} />
        </section>
      {/if}

      {#if idea.plan && (idea.plan.layers.length || idea.plan.additions.length)}
        <section class="pblock" aria-labelledby="ip">
          <h2 class="ls-zone" id="ip" tabindex="-1" data-anchor="zone:plan">The plan · revision {idea.plan.revision}</h2>
          {#each idea.plan.layers as layer (layer.id)}
            <PlanLayer idea={idea.id} {where} {layer} accepted={!!idea.accepted} disabledReason={refused}
              onchoose={(option) => record('idea choose', [idea!.id, layer.id, option, ...where], chooseUndone(layer, option))}
              onverdict={(verdict) => record(verdict === 'Keep' ? 'idea keep' : 'idea drop', [idea!.id, layer.id, ...where], verdictUndone(layer, verdict))}
              onimprove={(request) => record('idea improve', [idea!.id, layer.id, `--request=${request}`, ...where], verdictUndone(layer, 'Sending it back'))}
              onrework={(pending) => untrack(() => (reworks = { ...reworks, [layer.id]: pending }))} />
          {/each}
          {#if idea.plan.additions.length}
            <h3 class="sub" data-anchor="zone:additions">Additions the agent proposes</h3>
            {#each idea.plan.additions as addition (addition.id)}
              <PlanLayer idea={idea.id} {where} {addition} accepted={!!idea.accepted} disabledReason={refused}
                onverdict={(verdict) => record(verdict === 'Keep' ? 'idea keep' : 'idea drop', [idea!.id, addition.id, ...where], verdictUndone(addition, verdict))}
                onimprove={(request) => record('idea improve', [idea!.id, addition.id, `--request=${request}`, ...where], verdictUndone(addition, 'Sending it back'))}
                onrework={(pending) => untrack(() => (reworks = { ...reworks, [addition.id]: pending }))} />
            {/each}
          {/if}
        </section>
      {/if}

      {#if idea.plan && !idea.accepted}
        <section class="accept" aria-labelledby="ia">
          <h2 class="ls-zone" id="ia" data-anchor="zone:accept">Accept it</h2>
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
          <label class="done-when">
            <span>Done when <span class="hint">one condition a line: what must be true for it to be finished. Accepting confirms them, and a run for the task is held to them.</span></span>
            <textarea class="ls-text" name="accept-done-when" rows="4" bind:value={doneWhenText} disabled={!!refused}></textarea>
          </label>
          {#if !doneWhenCount}<p><span class="ls-tag needs">no done-when</span> It can still be accepted, but a team queue will refuse the task until it has some.</p>{/if}
          {#if undecided}<p><span class="ls-tag needs">{undecided} layer{undecided === 1 ? ' has' : 's have'} no option chosen</span> It can still be accepted; the plan says so where it is written.</p>{/if}
          <div class="ls-answers flush">
            <HaloButton label="Accept the plan" next="accept" solid={!acceptReason} glyph="✓" size="small" cmd={lineFor('idea accept', acceptLine)} disabledReason={acceptReason} onpress={() => void act('idea accept', acceptLine)} />
          </div>
        </section>
      {/if}

      {#if idea.notes?.length}
        <section class="pblock" aria-labelledby="in">
          <h2 class="ls-zone" id="in" data-anchor="zone:notes">Added since</h2>
          <ul class="notes">
            {#each idea.notes as note (note.id)}
              <li data-note={note.id}>
                <span class="nid">{note.id}</span>
                <span class="ntext">{note.text}</span>
                {#if note.taken}
                  <span class="ls-tag">read by a round</span>
                {:else}
                  <span class="ls-tag needs">not read by a round yet</span>
                  {#if !idea.accepted}
                    <HaloButton label={`Remove ${note.id}`} size="small" cmd={lineFor('idea note remove', [idea.id, note.id, ...where])} disabledReason={refused} onpress={() => void removeNote(note.id)} />
                  {/if}
                {/if}
              </li>
            {/each}
          </ul>
        </section>
      {/if}

      {#if !idea.accepted}
        <section class="pblock" aria-labelledby="ito">
          <h2 class="ls-zone" id="ito" data-anchor="zone:addto">Add to it</h2>
          {#if open}<p role="note"><span class="ls-tag needs">{open} question{open === 1 ? ' is' : 's are'} still open</span> The note is kept either way; a round waits until they are answered.</p>{/if}
          <label class="ls-vh" for="idea-more">What to add to it</label>
          <textarea id="idea-more" class="ls-text" name="idea-more" rows="3" placeholder="Something the next round should know. It is kept as you write it." bind:value={more} disabled={!!refused}></textarea>
          <label class="opt"><input type="checkbox" name="idea-refine-now" bind:checked={refineNow} disabled={!!refused} /> Refine now</label>
          <div class="ls-answers flush">
            <HaloButton label="Add it" size="small" solid={!refused && !!more.trim() && !adding} glyph="+" cmd={lineFor('idea add', addToWords)}
              disabledReason={refused ?? (adding ? 'adding it now' : more.trim() ? undefined : 'write what to add first')} onpress={() => void addTo()} />
          </div>
        </section>
      {/if}

      {#if !idea.accepted}
        <section class="pblock" aria-labelledby="im">
          <h2 class="ls-zone" id="im" data-anchor="zone:more">More</h2>
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

      {#if stop || arrived}
        <div class="ls-next">
          {#if arrived}
            <HaloButton label={arrived === 'questions' ? 'New questions: go to them' : `New revision ${idea.plan?.revision ?? ''}: go to it`} size="small" kind="needs" glyph="↑" cmd="changes nothing" onpress={() => goTo(arrived!)} />
          {/if}
          {#if stop}
          {#if left}<span class="left" aria-hidden="true">{left}</span>{/if}
          <HaloButton label={`Next: ${stopWords(stop)}`} size="small" solid glyph="↓" describedBy="ls-next-left"
            cmd={`changes nothing · ]${left ? ` · ${left} left to decide` : ''}`}
            disabledReason={refused && stop.kind === 'accept' ? refused : undefined} onpress={goNext} />
          <span class="ls-sr" id="ls-next-left">{left ? `${left} left to decide on this idea` : 'nothing left to decide on this idea'}</span>
          {/if}
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
