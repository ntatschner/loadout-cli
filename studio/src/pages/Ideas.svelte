<script lang="ts">
  // Ideas, a round at a time: the questions an agent asked, answered in the
  // page; a round started from here; the plan's layers and options chosen,
  // kept, dropped or sent back; and the idea accepted onto a project. Every
  // step is the command a terminal would type, run on the daemon, so a round
  // outlives the page that started it. Notes kept elsewhere come in as dumps.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { commands, readJson, SignedOut } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  let { rest }: { rest: string[] } = $props();

  interface Listed {
    id: string;
    title: string;
    project: string | null;
    stage: string;
    state: string;
    unanswered: number;
    running: boolean;
  }

  interface Question {
    id: string;
    question: string;
    why: string;
    options: string[];
    recommendation: string | null;
    answer: string | null;
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
    items: { number: number; title: string; excerpt: string; kind: string; recorded?: boolean }[];
  }

  let ideas = $state<Listed[] | null>(null);
  let idea = $state<Idea | null>(null);
  let dumps = $state<Dump[] | null>(null);
  let answers = $state<Record<string, string>>({});
  let problem = $state<string | null>(null);

  // #ideas/<list>/<id>, where the list is a project's slug or '~' for the
  // workspace-wide one; #ideas/+add and #ideas/+dumps for the rest.
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);
  const list = $derived(place ? null : (rest[0] ?? null));
  const id = $derived(place ? null : (rest[1] ?? null));
  const project = $derived(list && list !== '~' ? list : null);
  const where = $derived(project ? ['--project', project] : ['--global']);
  const listed = $derived(ideas?.find((one) => one.id === id && (one.project ?? null) === project) ?? null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadList() {
    try {
      ideas = (await readJson<{ ideas: Listed[] }>('idea list')).ideas;
    } catch (error) {
      fail(error);
    }
  }

  async function loadIdea(name: string, words: string[]) {
    try {
      idea = await readJson<Idea>('idea show', [name, ...words]);

      // A draft that has been saved is the answer now, and is shown as one.
      for (const question of idea.questions) {
        if (question.answer && answers[question.id] === question.answer) {
          delete answers[question.id];
        }
      }
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    void loadList();
  });

  $effect(() => {
    idea = null;
    answers = {};
    problem = null;

    if (id) {
      void loadIdea(id, where);
    }
  });

  $effect(() => {
    if (place === 'dumps') {
      readJson<{ dumps: Dump[] }>('idea dump list').then((read) => (dumps = read.dumps), fail);
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
        readJson<{ dumps: Dump[] }>('idea dump list').then((read) => (dumps = read.dumps), fail);
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

  // Prose goes joined to its option, as the dashboard sends it: the parser
  // refuses a value that starts with a dash given any other way.
  const answerWords = (question: string) => [id!, question, `--answer=${answers[question] ?? ''}`, ...where];
  const changedAnswers = $derived(idea?.questions.filter((one) => (answers[one.id] ?? '').trim() && answers[one.id] !== one.answer) ?? []);

  async function sendAnswers(thenRefine: boolean) {
    for (const question of changedAnswers) {
      if (!(await act('idea answer', answerWords(question.id)))) {
        return;
      }
    }

    if (thenRefine) {
      await act('idea refine', [id!, ...where]);
    }
  }

  const stateWord = (one: { running: boolean; unanswered: number; stage: string }) =>
    one.running ? 'round running' : one.unanswered > 0 ? 'waiting for you' : one.stage === 'accepted' ? 'accepted' : 'shaping';

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
</script>

<div class="head"><div><h1>Ideas</h1><span class="fact">{ideas ? `${ideas.length} being shaped` : 'ideas you are shaping, a round at a time'}</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Ideas">
    {#if ideas === null}
      <p class="calm" role="status">Reading…</p>
    {:else if ideas.length === 0}
      <p class="calm">No ideas being shaped. Drop one in.</p>
    {/if}

    {#each groups as [key, ones] (key)}
      <section class="store" aria-labelledby={`id-${key}`}>
        <h2 class="ls-zone" id={`id-${key}`}>{key === '~' ? 'Workspace-wide' : key}</h2>
        <ul class="slist">
          {#each ones as one (one.id)}
            <li>
              <a class="srow" href={href(one)} aria-current={id === one.id && (one.project ?? null) === project ? 'page' : undefined}>
                <span class="t">{one.title}</span>
                <span class="x">{one.unanswered ? `${one.unanswered} question${one.unanswered === 1 ? '' : 's'} for you` : one.stage}</span>
                <Signal word={stateWord(one)} />
              </a>
            </li>
          {/each}
        </ul>
      </section>
    {/each}
    <p class="calm small">The same as <code class="ls-cmd">loadout idea list</code>.</p>

    <section class="store" aria-labelledby="id-more">
      <h2 class="ls-zone" id="id-more">More</h2>
      <ul class="slist">
        <li><a class="srow" href="#ideas/+add" aria-current={place === 'add' ? 'page' : undefined}><span class="t">Drop an idea in</span></a></li>
        <li><a class="srow" href="#ideas/+dumps" aria-current={place === 'dumps' ? 'page' : undefined}><span class="t">Notes kept elsewhere</span></a></li>
      </ul>
    </section>
  </aside>

  <section class="memmain" aria-label="Idea">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if id && idea}
      <h2 class="topicname">{idea.plan?.title ?? idea.ask}</h2>
      {#if listed}<div class="jobhead"><Signal word={stateWord(listed)} /></div>{/if}
      {#if idea.plan}<p class="calm">{idea.plan.understanding}</p>{/if}
      <details class="more"><summary>What was asked</summary><p>{idea.ask}</p></details>
      <Command why="The same, in a terminal:" command={lineFor('idea show', [idea.id, ...where])} />

      {#if idea.accepted}
        <p class="calm">Accepted onto <strong>{idea.accepted.project}</strong> by {idea.accepted.by}. The plan is at <code class="ls-cmd">{idea.accepted.plan}</code>.</p>
      {/if}

      {#if idea.questions.length}
        <h3 class="ls-zone">Questions</h3>
        <ol class="questions">
          {#each idea.questions as question (question.id)}
            <li class="question">
              <p class="q"><span class="qid">{question.id}</span> {question.question}</p>
              <p class="calm small">{question.why}</p>
              {#if question.answer && answers[question.id] === undefined}
                <p class="answered">You answered: {question.answer}</p>
              {/if}
              {#if !idea.accepted}
                <fieldset class="choices">
                  <legend class="ls-sr">Answer {question.id}</legend>
                  {#each question.options as option, index (index)}
                    <label class="chk"><input type="radio" name={`a-${question.id}`} value={option} bind:group={answers[question.id]} /> <span>{option}{question.recommendation === option ? ' · recommended' : ''}</span></label>
                  {/each}
                </fieldset>
                <label class="fld"><span class="lab">Or in your own words</span><textarea rows="2" name={`own-${question.id}`} bind:value={answers[question.id]} spellcheck="true"></textarea></label>
                {#if question.recommendation && !question.options.includes(question.recommendation)}
                  <p class="calm small">Recommended: {question.recommendation}</p>
                {/if}
              {/if}
            </li>
          {/each}
        </ol>
        {#if !idea.accepted}
          <div class="ls-answers flush">
            <HaloButton label="Send answers and refine" solid size="small" cmd={`${changedAnswers.length} × loadout idea answer, then $ loadout idea refine ${idea.id}`} disabledReason={studio.watchOnly ? studio.watchOnlyReason : changedAnswers.length === 0 ? 'answer a question first' : undefined} onpress={() => void sendAnswers(true)} />
            <HaloButton label="Only save the answers" size="small" cmd={`${changedAnswers.length} × loadout idea answer`} disabledReason={studio.watchOnly ? studio.watchOnlyReason : changedAnswers.length === 0 ? 'answer a question first' : undefined} onpress={() => void sendAnswers(false)} />
          </div>
        {/if}
      {/if}

      {#if idea.plan}
        <h3 class="ls-zone">The plan · revision {idea.plan.revision}</h3>
        {#each idea.plan.layers as layer (layer.id)}
          <section class="layer" aria-labelledby={`ly-${layer.id}`}>
            <h4 id={`ly-${layer.id}`}><span class="qid">{layer.id}</span> {layer.name} <span class="calm small">· {layer.verdict}</span></h4>
            <p class="calm">{layer.purpose}</p>
            <div class="options">
              {#each layer.options as option (option.id)}
                <article class="option" class:chosen={layer.chosen === option.id}>
                  <p class="t"><span class="qid">{option.id}</span> {option.title}{option.recommended ? ' · recommended' : ''}{layer.chosen === option.id ? ' · chosen' : ''}</p>
                  <p class="calm small">{option.detail}</p>
                  {#if option.pros.length}<ul class="pros">{#each option.pros as pro, index (index)}<li>For: {pro}</li>{/each}</ul>{/if}
                  {#if option.cons.length}<ul class="cons">{#each option.cons as con, index (index)}<li>Against: {con}</li>{/each}</ul>{/if}
                  {#if !idea.accepted && layer.chosen !== option.id}
                    <HaloButton label="Choose this" size="small" cmd={lineFor('idea choose', [idea.id, layer.id, option.id, ...where])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void act('idea choose', [idea!.id, layer.id, option.id, ...where])} />
                  {/if}
                </article>
              {/each}
            </div>
            {#if !idea.accepted}
              <div class="ls-answers flush">
                <HaloButton label="Keep it" size="small" cmd={lineFor('idea keep', [idea.id, layer.id, ...where])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void act('idea keep', [idea!.id, layer.id, ...where])} />
                <HaloButton label="Drop it" size="small" cmd={lineFor('idea drop', [idea.id, layer.id, ...where])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void act('idea drop', [idea!.id, layer.id, ...where])} />
              </div>
              <CommandAction path="idea improve" title={`Ask for ${layer.id} to be reworked`} preset={{ id: idea.id, piece: layer.id, ...listFixed }} fixed={['id', 'piece', '--project', '--global']} label="Ask for it" />
            {/if}
          </section>
        {/each}

        {#if idea.plan.additions.length}
          <h3 class="ls-zone">Additions</h3>
          {#each idea.plan.additions as addition (addition.id)}
            <section class="layer" aria-labelledby={`ad-${addition.id}`}>
              <h4 id={`ad-${addition.id}`}><span class="qid">{addition.id}</span> {addition.title} <span class="calm small">· {addition.verdict}</span></h4>
              <p class="calm">{addition.why}</p>
              {#if !idea.accepted}
                <div class="ls-answers flush">
                  <HaloButton label="Keep it" size="small" cmd={lineFor('idea keep', [idea.id, addition.id, ...where])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void act('idea keep', [idea!.id, addition.id, ...where])} />
                  <HaloButton label="Drop it" size="small" cmd={lineFor('idea drop', [idea.id, addition.id, ...where])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void act('idea drop', [idea!.id, addition.id, ...where])} />
                </div>
              {/if}
            </section>
          {/each}
        {/if}

        {#if idea.plan.placement}
          <p class="calm">It would go on <strong>{idea.plan.placement.name}</strong>{idea.plan.placement.isNew ? ', a new project' : ''}: {idea.plan.placement.reason}</p>
        {/if}
      {/if}

      {#if !idea.accepted}
        <h3 class="ls-zone">Do</h3>
        <div class="acts">
          <CommandAction path="idea refine" title="Take it one round further" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Refine" open={!idea.questions.length && !idea.plan} />
          <CommandAction path="idea improve" title="Ask for the whole plan to be reworked" preset={{ id: idea.id, piece: 'plan', ...listFixed }} fixed={['id', 'piece', '--project', '--global']} label="Ask for it" />
          {#if idea.plan}
            <CommandAction path="idea accept" title="Accept the plan" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Accept" />
          {/if}
          <CommandAction path="idea dismiss" title="Clear the 'last round failed' marker" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Clear it" />
          <CommandAction path="idea remove" title="Forget this idea" preset={{ id: idea.id, ...listFixed }} fixed={['id', '--project', '--global']} label="Forget it" />
        </div>
        <p class="calm small">A round asks an agent and takes a minute or two. It runs on the daemon, so you can leave this page; a toast says when it has finished.</p>
      {/if}
    {:else if id}
      <p class="calm" role="status">Reading…</p>
    {:else if place === 'add'}
      <h2 class="topicname">Drop an idea in</h2>
      <CommandAction path="idea add" title="An idea, to be fleshed out by an agent later" open />
    {:else if place === 'dumps'}
      <h2 class="topicname">Notes kept elsewhere</h2>
      <p class="calm">Paste notes from wherever you keep them, and an agent splits them into ideas and tasks. Nothing is recorded until you apply the pieces.</p>
      <CommandAction path="idea dump add" title="Drop notes in" open />
      {#if dumps === null}
        <p class="calm" role="status">Reading…</p>
      {:else}
        {#each dumps as dump (dump.id)}
          <section class="layer" aria-labelledby={`dp-${dump.id}`}>
            <h4 id={`dp-${dump.id}`}>{dump.id} <span class="calm small">· {dump.source} · {new Date(dump.captured).toLocaleString()}</span></h4>
            <ul class="plain">
              {#each dump.items as item (item.number)}<li><span class="qid">{item.number}</span> {item.title} <span class="calm small">· {item.kind}{item.recorded ? ' · recorded' : ''}</span></li>{/each}
            </ul>
            <div class="acts">
              <CommandAction path="idea dump show" title="Read it" preset={{ id: dump.id }} fixed={['id']} label="Show" />
              <CommandAction path="idea dump apply" title="Record its pieces" preset={{ id: dump.id }} fixed={['id']} label="Record" />
              <CommandAction path="idea dump split" title="Split it again" preset={{ id: dump.id }} fixed={['id']} label="Split" />
            </div>
          </section>
        {:else}
          <p class="calm">No notes dropped in yet.</p>
        {/each}
        <p class="calm small">From <code class="ls-cmd">loadout idea dump list</code>.</p>
      {/if}
    {:else if ideas?.length}
      <p class="calm">Choose an idea to answer its questions, shape its plan or accept it.</p>
    {/if}
  </section>
</div>
