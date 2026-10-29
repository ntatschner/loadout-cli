<script lang="ts">
  // The shell every page sits in: the rail with its live counts, the page,
  // notifications, the palette and Ambient. Signing in is the daemon's: a
  // browser it does not know is shown how to open Studio signed in, and
  // nothing else.
  import { onMount, untrack } from 'svelte';
  import BesideGrid from './lib/components/BesideGrid.svelte';
  import PageBeside from './lib/components/PageBeside.svelte';
  import SideRail from './lib/components/SideRail.svelte';
  import Toasts from './lib/components/Toasts.svelte';
  import CommandPalette from './lib/components/CommandPalette.svelte';
  import Dialog from './lib/components/Dialog.svelte';
  import HaloButton from './lib/components/HaloButton.svelte';
  import Home from './pages/Home.svelte';
  import Ambient from './pages/Ambient.svelte';
  import Sessions from './pages/Sessions.svelte';
  import LaunchSheet from './pages/LaunchSheet.svelte';
  import { search } from './lib/search';
  import { approvePlan, askAgent, askLoadout, type PaletteAsk } from './lib/ask';
  import { conversation } from './lib/askConversation.svelte';
  import { commands } from './lib/api';
  import { runJob } from './lib/jobs';
  import type { Chosen } from './lib/tasks';
  import type { Conversation } from './lib/api';
  import Settings from './pages/Settings.svelte';
  import Appearance from './pages/Appearance.svelte';
  import Memory from './pages/Memory.svelte';
  import Commands from './pages/Commands.svelte';
  import Projects from './pages/Projects.svelte';
  import Ideas from './pages/Ideas.svelte';
  import Teams from './pages/Teams.svelte';
  import Events from './pages/Events.svelte';
  import Context from './pages/Context.svelte';
  import Spend from './pages/Spend.svelte';
  import Elsewhere from './pages/Elsewhere.svelte';
  import SignedOut from './pages/SignedOut.svelte';
  import { studio } from './lib/state.svelte';
  import { router } from './lib/router.svelte';
  import { paletteGroups } from './lib/paletteGroups';
  import { THEME_NAMES, type Theme } from './lib/studio';
  import { reportLink } from './lib/report';
  import { tokens } from './lib/glyphs';
  import type { RailPage } from './lib/types';
  import ModeBar from './lib/components/ModeBar.svelte';
  import FieldMap from './lib/components/FieldMap.svelte';
  import { resolveBackdrop } from './lib/backdrop';
  import Quiet from './pages/Quiet.svelte';
  import QuietIdeas from './pages/quiet/QuietIdeas.svelte';
  import QuietProjects from './pages/quiet/QuietProjects.svelte';
  import QuietContext from './pages/quiet/QuietContext.svelte';
  import QuietSettings from './pages/quiet/QuietSettings.svelte';
  import QuietMemory from './pages/quiet/QuietMemory.svelte';
  import QuietSpend from './pages/quiet/QuietSpend.svelte';
  import { besideReturn, isModeKey, isSixPage, motionNow, PAGE_TITLES, type SixPage, type Waiting } from './lib/modes';
  import { besideOf } from './lib/reveal';
  import { reveal, revealParts } from './lib/reveal.svelte';
  import { readSplit, SPLIT_KEYS } from './lib/splits';

  let palette = $state(false);
  let appearance = $state(false);
  let launching = $state(false);
  let ambient = $state(false);
  let launchTask = $state('');
  let launchChosen = $state<Chosen | null>(null);
  /** The conversation the sheet is resuming, for 'Resume with changes'; null for a new session. */
  let launchResuming = $state<Conversation | null>(null);
  /** How much of the width a page beside the terminals or the board was left with, in this browser. */
  let besideSplit = $state(readSplit(studio.kept(SPLIT_KEYS.beside, ''), 1)?.[0] ?? null);

  function keepBeside(to: number) {
    besideSplit = to;
    studio.keep(SPLIT_KEYS.beside, JSON.stringify([to]));
  }

  onMount(() => {
    // The mode is this browser's own, so the layout is right before the prefs are read.
    document.documentElement.setAttribute('data-mode', studio.mode);
    void studio.start();

    return () => studio.stop();
  });

  const sessionLines = $derived(studio.lines.filter((line) => line.kind === 'session'));
  const running = $derived(sessionLines.filter((line) => line.stage === 2).length);
  const teamWaiting = $derived(studio.waiting.filter((line) => line.kind === 'team run').length);
  const teamRunning = $derived(studio.lines.filter((line) => line.kind === 'team run' && line.run?.running).length);
  const ideaCount = $derived(studio.lines.filter((line) => line.kind === 'idea').length);
  const ideaWaiting = $derived(studio.waiting.filter((line) => line.kind === 'idea').length);

  const pages: RailPage[] = $derived([
    { key: 'home', name: 'Home', fact: studio.waiting.length ? `${studio.waiting.length} waiting on you` : 'all calm', need: studio.waiting.length > 0 },
    { key: 'sessions', name: 'Sessions', fact: `${running} running`, need: false },
    { key: 'ideas', name: 'Ideas', fact: ideaWaiting ? `${ideaWaiting} waiting on you` : `${ideaCount} being shaped`, need: ideaWaiting > 0 },
    { key: 'teams', name: 'Teams', fact: teamWaiting ? `${teamWaiting} waiting on you` : `${teamRunning} running`, need: teamWaiting > 0 },
    { key: 'events', name: 'Events', fact: studio.unreadEvents ? `${studio.unreadEvents} unread` : 'what needed you', need: !!studio.unreadEvents },
    { key: 'memory', name: 'Memory', fact: 'by topic' },
    { key: 'context', name: 'Context', fact: 'what a session reads' },
    { key: 'spend', name: 'Spend', fact: studio.spentMonth === null ? 'tokens and cost' : `${tokens(studio.spentMonth)} tokens this month` },
    { key: 'projects', name: 'Projects', fact: '' },
    { key: 'commands', name: 'Commands', fact: studio.jobs.some((job) => job.state === 'running') ? `${studio.jobs.filter((job) => job.state === 'running').length} running` : 'everything else' },
    { key: 'settings', name: 'Settings', fact: studio.update?.pending ? 'update pending' : studio.update?.isNewer ? `${studio.update.available} available` : '' },
  ]);

  const current = $derived(router.route.page === 'session' ? 'sessions' : router.route.page);
  const theme = $derived(((studio.prefs?.theme as Theme | null) ?? null));

  function openLaunch(task = '', chosen: Chosen | null = null) {
    if (ambient) {
      ambient = false;
    }

    launchTask = task;
    launchChosen = chosen;
    launchResuming = null;
    launching = true;
  }

  function openResume(conversation: Conversation) {
    if (ambient) {
      ambient = false;
    }

    launchTask = '';
    launchChosen = null;
    launchResuming = conversation;
    launching = true;
  }

  // What is waiting on you, sessions and team runs together, for the ModeBar.
  const modeWaiting: Waiting[] = $derived(
    studio.waiting
      .filter((line) => line.kind === 'session' || line.kind === 'team run')
      .map((line) => ({
        kind: line.kind === 'session' ? ('session' as const) : ('run' as const),
        id: line.ref,
        label: line.kind === 'session' ? `${line.title} ${line.approval ? 'needs approval' : 'is waiting for you'}` : (line.ask ?? line.title),
        at: line.askedAt ?? 0,
      })),
  );

  // What is behind the page: resolveBackdrop() from the setting and all that
  // overrides it, written as data-fieldmap for the CSS. The attributes
  // applyPrefs() sets, the system's reduced motion, the tab and where the
  // keyboard is all change outside Svelte, so each one bumps `looked`.
  let looked = $state(0);
  const look = () => looked++;

  onMount(() => {
    const watching = new MutationObserver(look);
    watching.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-motion', 'data-backdrop'] });
    const reduced = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;
    reduced?.addEventListener?.('change', look);
    document.addEventListener('visibilitychange', look);
    document.addEventListener('focusin', look);
    document.addEventListener('focusout', look);

    return () => {
      watching.disconnect();
      reduced?.removeEventListener?.('change', look);
      document.removeEventListener('visibilitychange', look);
      document.removeEventListener('focusin', look);
      document.removeEventListener('focusout', look);
    };
  });

  const fieldmap = $derived.by(() => {
    void looked;
    const html = document.documentElement;

    return resolveBackdrop({
      setting: html.getAttribute('data-backdrop'),
      theme: html.getAttribute('data-theme') ?? 'dark',
      mode: studio.mode,
      ambient,
      motion: motionNow(),
      waiting: modeWaiting.length,
      hidden: document.visibilityState === 'hidden',
      terminalFocused: !!document.activeElement?.closest('.xterm'),
    });
  });

  $effect(() => document.documentElement.setAttribute('data-fieldmap', fieldmap));

  /** The oldest question, wherever the current mode shows it, so no focus mode ever hides one. */
  function goWaiting(waiting: Waiting) {
    reveal(waiting.kind === 'session' ? { kind: 'session', id: waiting.id } : { kind: 'run', id: waiting.id });
  }

  // Ctrl+Shift+M, caught before anything on the page, a terminal included,
  // can take it: a terminal would otherwise send it to its session.
  function modeKey(event: KeyboardEvent) {
    if (isModeKey(event)) {
      event.preventDefault();
      event.stopPropagation();
      studio.cycleMode();
    }
  }

  // Which page each mode draws for a route: the deck for Home and the
  // sessions in Terminals, the board for Home in Teams, the quiet column for
  // Home in Minimal. Everything else is the page it always was.
  const page = $derived(router.route.page);
  const deck = $derived(studio.mode === 'terminals' && (page === 'home' || page === 'sessions' || page === 'session'));
  // Pure Focus: Home, Sessions, a session, Teams and a run are the desk;
  // a team's page, the remedies and the bin stay pages, in the flat look.
  const pure = $derived(
    studio.mode === 'pure' && (page === 'home' || page === 'sessions' || page === 'session' || (page === 'teams' && !router.route.rest[0]?.startsWith('+'))),
  );
  const board = $derived(studio.mode === 'teams' && page === 'home');
  const quiet = $derived(studio.mode === 'minimal' && page === 'home');
  // The six pages follow the mode: in Terminals and Teams they open beside
  // the deck or the board, which stay where they are and stay live.
  const six = $derived(isSixPage(page));
  // What is open beside the deck or the board: one of the six, a run or a
  // team's page beside the terminals, or a session beside the board. A
  // session's address in Terminals can name the page it keeps open, so a
  // launch from that page leaves it where it was.
  const besideRoute = $derived(besideOf(studio.mode, router.route));
  const deckHost = $derived(deck || (studio.mode === 'terminals' && !!besideRoute));
  // In Pure Focus each of the six pages is a desk; its places (+add and the rest) stay pages, in the flat look.
  const pureDesk = $derived(studio.mode === 'pure' && six && !router.route.rest[0]?.startsWith('+'));
  const boardHost = $derived(board || (studio.mode === 'teams' && !!besideRoute));
  const besideTitle = $derived(
    !besideRoute ? '' : isSixPage(besideRoute.page) ? PAGE_TITLES[besideRoute.page] : besideRoute.page === 'session' ? 'Session' : besideRoute.rest[0] === '+team' ? 'Team' : 'Run',
  );

  // The deck's session, kept while a page is open beside it, so opening one never changes the panes.
  let deckId = $state<string | null>(null);
  // Where closing a page beside goes back to.
  let lastOther = $state<string[]>(['home']);

  $effect(() => {
    const route = router.route;

    untrack(() => {
      // Only what is beside the deck or board is left out: closing it goes back to what was there.
      lastOther = besideReturn(lastOther, route, !route.beside && !!besideRoute);

      if (route.page === 'session' || route.page === 'home' || route.page === 'sessions') {
        deckId = route.page === 'session' ? (route.rest[0] ?? null) : null;
      }
    });
  });

  function closeBeside() {
    const route = router.route;

    if (route.beside) {
      router.go(route.page, ...route.rest);
    } else {
      router.go(...lastOther);
    }
  }

  function keys(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      palette = !palette;

      return;
    }

    const target = event.target as HTMLElement | null;
    const typing = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable || target.closest('.xterm'));

    // Pure Focus has its own single keys, every one shown, and these are not among them.
    if (typing || pure || pureDesk || event.ctrlKey || event.metaKey || event.altKey || !studio.keys || document.querySelector('.ls-dialog')) {
      return;
    }

    if (event.key.toLowerCase() === 'a' && router.route.page === 'home') {
      ambient = !ambient;
    } else if (event.key.toLowerCase() === 'n' && !studio.watchOnly) {
      // The sheet opens over what is showing, which the launch then adds to.
      event.preventDefault();
      openLaunch();
    }
  }

  // Asking Loadout from the palette. A plan is carried out only from its
  // card's Approve, through the same commands the terminal would run.
  const paletteAsk: PaletteAsk = {
    conversation,
    asker: askLoadout,
    agent: (query, resume) => askAgent(query, resume),
    approve: (plan, team, cap) =>
      approvePlan(plan, team, cap, {
        start: (path, words) => commands.run(path, words),
        run: (path, words) => runJob(path, words),
        openTerminal: async (project, task) => {
          if (!(await studio.startSession({ project, taskId: task, columns: 120, rows: 32 }, `loadout ${project}`))) {
            throw new Error(`The terminal for ${task} on ${project} did not open.`);
          }
        },
      }),
  };

  const groups = $derived(
    paletteGroups(studio, revealParts, {
      launch: () => openLaunch(),
      ambient: () => {
        router.go('home');
        ambient = true;
      },
      appearance: () => (appearance = true),
    }),
  );
</script>

<svelte:window onkeydown={keys} onkeydowncapture={modeKey} />

{#if studio.signedOut}
  <SignedOut />
{:else}
  <FieldMap shown={fieldmap} />
  <a class="skip" href="#main" onclick={(event) => { event.preventDefault(); document.getElementById('main')?.focus(); }}>Skip to the page</a>
  <SideRail {pages} {current} onsearch={() => (palette = true)}>
    {#snippet foot()}
      <a class="ls-rl ls-report" href={reportLink(studio.about)} target="_blank" rel="noopener noreferrer">
        <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M5 17.5 V3 M5 3.5 H15 L12.5 7 L15 10.5 H5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        <span class="n">Report a problem</span><span class="f">opens GitHub</span>
      </a>
      <button type="button" class="ls-rl ls-appearance" onclick={() => (appearance = true)}>
        <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 3a7 7 0 1 0 0 14a3.5 3.5 0 0 1 0-7a3.5 3.5 0 0 0 0-7z M10 3a7 7 0 0 1 0 14" fill="none" stroke="currentColor" stroke-width="1.6" /></svg>
        <span class="n">Appearance</span><span class="f">{theme ? THEME_NAMES[theme] : 'Follows the system'} · {studio.prefs?.accent ?? 'cyan'}</span>
      </button>
    {/snippet}
  </SideRail>

  <div id="modebar">
    <ModeBar mode={studio.mode} onmode={(mode) => studio.setMode(mode as typeof studio.mode)} onsearch={() => (palette = true)} waiting={modeWaiting} onwaiting={goWaiting} />
  </div>

  <main id="main" class="page" tabindex="-1">
    {#if studio.watchOnly}
      <div class="watchbanner" role="note">
        <p>Studio is only watching, so nothing here can change a run, an idea or a task. Each action shows the command to type in a terminal instead.</p>
        <code class="ls-cmd">started with loadout dashboard --watch-only</code>
      </div>
    {/if}
    {#if studio.offline}
      <p class="offline" role="alert">{studio.offline}</p>
    {/if}
    {#if studio.newVersion}
      <div class="reloadnote" role="status">
        <p>The daemon is now on {studio.newVersion}. This page is from the version before, so reload it to use the new one.</p>
        <HaloButton label="Reload" size="small" onpress={() => location.reload()} />
        <HaloButton label="Later" size="small" onpress={() => (studio.newVersion = null)} />
      </div>
    {/if}

    {#if !studio.loaded}
      <p class="calm" role="status">Reading what the daemon knows…</p>
    {:else if pure}
      <!-- One desk for every route it covers, so moving between them keeps
           the list, the view and its terminal. -->
      {#await import('./pages/PureFocus.svelte')}
        <p class="calm" role="status">Opening Pure Focus…</p>
      {:then view}
        <view.default page={page} named={page === 'session' || page === 'teams' ? (router.route.rest[0] ?? null) : null} onlaunch={() => openLaunch()} />
      {/await}
    {:else if pureDesk && isSixPage(page)}
      {#await import('./pages/PageDesk.svelte')}
        <p class="calm" role="status">Opening the desk…</p>
      {:then desk}
        {#key page}
          <desk.default {page} rest={router.route.rest} onlaunch={(chosen) => openLaunch('', chosen ?? null)} />
        {/key}
      {/await}
    {:else if deckHost}
      <!-- One deck for Home, Sessions, a session's page and any of the six
           pages beside it, so moving between them keeps its panes and their
           connections. -->
      <BesideGrid beside={besideRoute ? besidePage : undefined} split={besideSplit} onsplit={keepBeside}>
        {#snippet focus()}
          {#await import('./pages/TerminalFocus.svelte')}
            <p class="calm" role="status">Opening the terminals…</p>
          {:then view}
            <view.default id={deckId} onlaunch={() => openLaunch()} />
          {/await}
        {/snippet}
      </BesideGrid>
    {:else if boardHost}
      <BesideGrid beside={besideRoute ? besidePage : undefined} split={besideSplit} onsplit={keepBeside}>
        {#snippet focus()}<Teams rest={[]} />{/snippet}
      </BesideGrid>
    {:else if quiet}
      <Quiet />
    {:else if router.route.page === 'home'}
      <Home onambient={() => (ambient = true)} onlaunch={openLaunch} />
    {:else if router.route.page === 'sessions'}
      <Sessions onlaunch={() => openLaunch()} onresume={openResume} />
    {:else if router.route.page === 'session'}
      <!-- Loaded when first needed: the terminal and its renderer are most of
           Studio's size, and only a session's page uses them. -->
      {#await import('./pages/SessionView.svelte')}
        <p class="calm" role="status">Opening the terminal…</p>
      {:then view}
        <!-- Made again for each session: one page's connection, terminal
             and state must not carry over to the next. -->
        {#key router.route.rest[0]}
          <view.default id={router.route.rest[0]} />
        {/key}
      {/await}
    {:else if studio.mode === 'minimal' && six}
      <!-- Each of the six pages as one quiet column; the route is the same, so a deep link opens with everything shown. -->
      {#key page}
        {#if page === 'ideas'}
          <QuietIdeas rest={router.route.rest} />
        {:else if page === 'projects'}
          <QuietProjects rest={router.route.rest} onlaunch={(chosen) => openLaunch('', chosen ?? null)} />
        {:else if page === 'context'}
          <QuietContext rest={router.route.rest} />
        {:else if page === 'settings'}
          <QuietSettings rest={router.route.rest} />
        {:else if page === 'memory'}
          <QuietMemory rest={router.route.rest} />
        {:else}
          <QuietSpend />
        {/if}
      {/key}
    {:else if isSixPage(page)}
      {@render sixPage(page, router.route.rest)}
    {:else if router.route.page === 'teams'}
      <Teams rest={router.route.rest} />
    {:else if router.route.page === 'events'}
      <Events query={router.route.query ?? ''} />
    {:else if router.route.page === 'commands'}
      <Commands rest={router.route.rest} onlaunch={() => openLaunch()} />
    {:else}
      <Elsewhere />
    {/if}
  </main>

  {#snippet sixPage(at: SixPage, rest: string[])}
    {#if at === 'settings'}
      <Settings {rest} />
    {:else if at === 'memory'}
      <Memory slug={rest[0] ?? null} scope={rest[1] ?? null} name={rest[2] ?? null} />
    {:else if at === 'spend'}
      <Spend />
    {:else if at === 'context'}
      <Context {rest} />
    {:else if at === 'ideas'}
      <Ideas {rest} />
    {:else if at === 'projects'}
      <Projects {rest} onlaunch={(chosen) => openLaunch('', chosen ?? null)} onresume={openResume} />
    {/if}
  {/snippet}

  {#snippet besidePage()}
    {#if besideRoute}
      <PageBeside page={besideTitle} beside={studio.mode === 'teams' ? 'the runs board' : 'your terminals'} onclose={closeBeside}>
        {#if isSixPage(besideRoute.page)}
          {@render sixPage(besideRoute.page, besideRoute.rest)}
        {:else if besideRoute.page === 'teams'}
          <!-- A run or a team's page, as the Teams page shows it outside Team Focus. -->
          <Teams rest={besideRoute.rest} />
        {:else if besideRoute.page === 'session'}
          {#await import('./pages/SessionView.svelte')}
            <p class="calm" role="status">Opening the terminal…</p>
          {:then view}
            {#key besideRoute.rest[0]}
              <view.default id={besideRoute.rest[0]} />
            {/key}
          {/await}
        {/if}
      </PageBeside>
    {/if}
  {/snippet}

  <Toasts place={pure ? 'pure' : pureDesk ? 'desk' : page === 'session' ? 'session' : null} />

  {#if ambient}
    <Ambient onleave={() => (ambient = false)} onlaunch={() => openLaunch()} />
  {/if}
  {#if palette}
    <CommandPalette {groups} search={(query) => search(query, revealParts)} ask={paletteAsk} onclose={() => (palette = false)} />
  {/if}
  {#if appearance}
    <Dialog title="Appearance" why="Every Studio screen follows these, in every browser: they are kept in Loadout's config." cancelLabel="Done" actions={[]} onclose={() => (appearance = false)}>
      <Appearance />
      <div class="acts"><HaloButton label="Done" size="small" onpress={() => (appearance = false)} /></div>
    </Dialog>
  {/if}
  {#if launching}
    <LaunchSheet task={launchTask} chosen={launchChosen} resuming={launchResuming} onclose={() => (launching = false)} />
  {/if}
  {#if studio.resumeAsk}
    {@const asking = studio.resumeAsk}
    {#if asking.question.kind === 'elsewhere'}
      <Dialog
        title="It looks to be running already"
        why={`‘${asking.question.conversation.title}’ was written to a moment ago, or its launch is still running, somewhere other than Studio. A second copy writes to the same conversation, and the two will not see each other's turns.`}
        actions={[{ label: 'Resume it anyway', cmd: 'opens a second copy here', onpress: () => asking.answer(true) }]}
        onclose={() => asking.answer(false)}
      />
    {:else}
      <Dialog
        title="Register this folder?"
        why={`That conversation ran in ${asking.question.directory}, which is not a Loadout project. Resuming it registers that folder first, as ‘loadout project add’ would, and then carries it on there.`}
        actions={[{ label: 'Register and resume', solid: true, cmd: `$ loadout project add "${asking.question.directory}"`, onpress: () => asking.answer(true) }]}
        onclose={() => asking.answer(false)}
      />
    {/if}
  {/if}
{/if}

