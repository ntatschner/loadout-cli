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
  import type { Chosen } from './lib/tasks';
  import type { Conversation } from './lib/api';
  import Settings from './pages/Settings.svelte';
  import Appearance from './pages/Appearance.svelte';
  import Memory from './pages/Memory.svelte';
  import Commands from './pages/Commands.svelte';
  import Projects from './pages/Projects.svelte';
  import Ideas from './pages/Ideas.svelte';
  import Teams from './pages/Teams.svelte';
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
  import Quiet from './pages/Quiet.svelte';
  import QuietIdeas from './pages/quiet/QuietIdeas.svelte';
  import QuietProjects from './pages/quiet/QuietProjects.svelte';
  import QuietContext from './pages/quiet/QuietContext.svelte';
  import QuietSettings from './pages/quiet/QuietSettings.svelte';
  import QuietMemory from './pages/quiet/QuietMemory.svelte';
  import QuietSpend from './pages/quiet/QuietSpend.svelte';
  import { besideReturn, isModeKey, isSixPage, PAGE_TITLES, type Waiting } from './lib/modes';

  let palette = $state(false);
  let appearance = $state(false);
  let launching = $state(false);
  let ambient = $state(false);
  let launchTask = $state('');
  let launchChosen = $state<Chosen | null>(null);
  /** The conversation the sheet is resuming, for 'Resume with changes'; null for a new session. */
  let launchResuming = $state<Conversation | null>(null);

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
    { key: 'memory', name: 'Memory', fact: 'by topic' },
    { key: 'context', name: 'Context', fact: 'what a session reads' },
    { key: 'spend', name: 'Spend', fact: studio.spentMonth === null ? 'tokens and cost' : `${tokens(studio.spentMonth)} tokens this month` },
    { key: 'projects', name: 'Projects', fact: '' },
    { key: 'commands', name: 'Commands', fact: studio.jobs.some((job) => job.state === 'running') ? `${studio.jobs.filter((job) => job.state === 'running').length} running` : 'everything else' },
    { key: 'settings', name: 'Settings', fact: '' },
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

  /** The oldest question, wherever the current mode shows it, so no focus mode ever hides one. */
  function goWaiting(waiting: Waiting) {
    if (studio.mode === 'minimal') {
      router.go('home');
      studio.focusing = { kind: waiting.kind === 'session' ? 'session' : 'team run', id: waiting.id };
    } else if (waiting.kind === 'session') {
      router.go('session', waiting.id);
    } else {
      router.go('teams', waiting.id);
    }
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
  const deckHost = $derived(deck || (studio.mode === 'terminals' && six));
  // In Pure Focus each of the six pages is a desk; its places (+add and the rest) stay pages, in the flat look.
  const pureDesk = $derived(studio.mode === 'pure' && six && !router.route.rest[0]?.startsWith('+'));
  const boardHost = $derived(board || (studio.mode === 'teams' && six));

  // The deck's session, kept while a page is open beside it, so opening one never changes the panes.
  let deckId = $state<string | null>(null);
  // Where closing a page beside goes back to.
  let lastOther = $state<string[]>(['home']);

  $effect(() => {
    const route = router.route;

    untrack(() => {
      lastOther = besideReturn(lastOther, route);

      if (route.page === 'session' || route.page === 'home' || route.page === 'sessions') {
        deckId = route.page === 'session' ? (route.rest[0] ?? null) : null;
      }
    });
  });

  function closeBeside() {
    router.go(...lastOther);
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
      event.preventDefault();
      router.go('sessions');
      openLaunch();
    }
  }

  const groups = $derived(
    paletteGroups(studio, router.go.bind(router), {
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
      <BesideGrid beside={six ? besidePage : undefined}>
        {#snippet focus()}
          {#await import('./pages/TerminalFocus.svelte')}
            <p class="calm" role="status">Opening the terminals…</p>
          {:then view}
            <view.default id={deckId} onlaunch={() => openLaunch()} />
          {/await}
        {/snippet}
      </BesideGrid>
    {:else if boardHost}
      <BesideGrid beside={six ? besidePage : undefined}>
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
          <QuietProjects rest={router.route.rest} onlaunch={(chosen) => { router.go('sessions'); openLaunch('', chosen ?? null); }} />
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
    {:else if six}
      {@render sixPage()}
    {:else if router.route.page === 'teams'}
      <Teams rest={router.route.rest} />
    {:else if router.route.page === 'commands'}
      <Commands rest={router.route.rest} onlaunch={() => { router.go('sessions'); openLaunch(); }} />
    {:else}
      <Elsewhere />
    {/if}
  </main>

  {#snippet sixPage()}
    {#if page === 'settings'}
      <Settings rest={router.route.rest} />
    {:else if page === 'memory'}
      <Memory slug={router.route.rest[0] ?? null} scope={router.route.rest[1] ?? null} name={router.route.rest[2] ?? null} />
    {:else if page === 'spend'}
      <Spend />
    {:else if page === 'context'}
      <Context rest={router.route.rest} />
    {:else if page === 'ideas'}
      <Ideas rest={router.route.rest} />
    {:else if page === 'projects'}
      <Projects rest={router.route.rest} onlaunch={(chosen) => { router.go('sessions'); openLaunch('', chosen ?? null); }} onresume={openResume} />
    {/if}
  {/snippet}

  {#snippet besidePage()}
    {#if isSixPage(page)}
      <PageBeside page={PAGE_TITLES[page]} beside={studio.mode === 'teams' ? 'the runs board' : 'your terminals'} onclose={closeBeside}>
        {@render sixPage()}
      </PageBeside>
    {/if}
  {/snippet}

  <Toasts />

  {#if ambient}
    <Ambient onleave={() => (ambient = false)} onlaunch={() => { router.go('sessions'); openLaunch(); }} />
  {/if}
  {#if palette}
    <CommandPalette {groups} search={(query) => search(query, router.go.bind(router))} onclose={() => (palette = false)} />
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

