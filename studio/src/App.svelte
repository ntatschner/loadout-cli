<script lang="ts">
  // The shell every page sits in: the rail with its live counts, the page,
  // notifications, the palette and Ambient. Signing in is the daemon's: a
  // browser it does not know is shown how to open Studio signed in, and
  // nothing else.
  import { onMount } from 'svelte';
  import SideRail from './lib/components/SideRail.svelte';
  import Toasts from './lib/components/Toasts.svelte';
  import CommandPalette from './lib/components/CommandPalette.svelte';
  import Dialog from './lib/components/Dialog.svelte';
  import HaloButton from './lib/components/HaloButton.svelte';
  import Home from './pages/Home.svelte';
  import Ambient from './pages/Ambient.svelte';
  import Sessions from './pages/Sessions.svelte';
  import LaunchSheet from './pages/LaunchSheet.svelte';
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
  import type { RailPage } from './lib/types';

  let palette = $state(false);
  let appearance = $state(false);
  let launching = $state(false);
  let ambient = $state(false);
  let launchTask = $state('');

  onMount(() => {
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
    { key: 'spend', name: 'Spend', fact: 'tokens and cost' },
    { key: 'projects', name: 'Projects', fact: '' },
    { key: 'commands', name: 'Commands', fact: studio.jobs.some((job) => job.state === 'running') ? `${studio.jobs.filter((job) => job.state === 'running').length} running` : 'everything else' },
    { key: 'settings', name: 'Settings', fact: '' },
  ]);

  const current = $derived(router.route.page === 'session' ? 'sessions' : router.route.page);
  const theme = $derived(((studio.prefs?.theme as Theme | null) ?? null));

  function openLaunch(task = '') {
    if (ambient) {
      ambient = false;
    }

    launchTask = task;
    launching = true;
  }

  function keys(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      palette = !palette;

      return;
    }

    const target = event.target as HTMLElement | null;
    const typing = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable || target.closest('.xterm'));

    if (typing || event.ctrlKey || event.metaKey || event.altKey || !studio.keys || document.querySelector('.ls-dialog')) {
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

<svelte:window onkeydown={keys} />

{#if studio.signedOut}
  <SignedOut />
{:else}
  <a class="skip" href="#main" onclick={(event) => { event.preventDefault(); document.getElementById('main')?.focus(); }}>Skip to the page</a>
  <SideRail {pages} {current} onsearch={() => (palette = true)}>
    {#snippet foot()}
      <a class="ls-rl ls-report" href={reportLink(studio.about)} target="_blank" rel="noopener noreferrer">
        <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M5 17.5 V3 M5 3.5 H15 L12.5 7 L15 10.5 H5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        <span class="n">Report a problem</span><span class="f">Studio is a preview · opens GitHub</span>
      </a>
      <button type="button" class="ls-rl ls-appearance" onclick={() => (appearance = true)}>
        <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 3a7 7 0 1 0 0 14a3.5 3.5 0 0 1 0-7a3.5 3.5 0 0 0 0-7z M10 3a7 7 0 0 1 0 14" fill="none" stroke="currentColor" stroke-width="1.6" /></svg>
        <span class="n">Appearance</span><span class="f">{theme ? THEME_NAMES[theme] : 'Follows the system'} · {studio.prefs?.accent ?? 'cyan'}</span>
      </button>
    {/snippet}
  </SideRail>

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
    {:else if router.route.page === 'home'}
      <Home onambient={() => (ambient = true)} onlaunch={openLaunch} />
    {:else if router.route.page === 'sessions'}
      <Sessions onlaunch={() => openLaunch()} />
    {:else if router.route.page === 'session'}
      <!-- Loaded when first needed: the terminal and its renderer are most of
           Studio's size, and only a session's page uses them. -->
      {#await import('./pages/SessionView.svelte')}
        <p class="calm" role="status">Opening the terminal…</p>
      {:then view}
        <view.default id={router.route.rest[0]} />
      {/await}
    {:else if router.route.page === 'settings'}
      <Settings rest={router.route.rest} />
    {:else if router.route.page === 'memory'}
      <Memory slug={router.route.rest[0] ?? null} scope={router.route.rest[1] ?? null} name={router.route.rest[2] ?? null} />
    {:else if router.route.page === 'spend'}
      <Spend />
    {:else if router.route.page === 'context'}
      <Context rest={router.route.rest} />
    {:else if router.route.page === 'teams'}
      <Teams rest={router.route.rest} />
    {:else if router.route.page === 'ideas'}
      <Ideas rest={router.route.rest} />
    {:else if router.route.page === 'projects'}
      <Projects rest={router.route.rest} onlaunch={() => { router.go('sessions'); openLaunch(); }} />
    {:else if router.route.page === 'commands'}
      <Commands rest={router.route.rest} onlaunch={() => { router.go('sessions'); openLaunch(); }} />
    {:else}
      <Elsewhere />
    {/if}
  </main>

  <Toasts />

  {#if ambient}
    <Ambient onleave={() => (ambient = false)} onlaunch={() => { router.go('sessions'); openLaunch(); }} />
  {/if}
  {#if palette}
    <CommandPalette {groups} onclose={() => (palette = false)} />
  {/if}
  {#if appearance}
    <Dialog title="Appearance" why="Every Studio screen follows these, in every browser: they are kept in Loadout's config." cancelLabel="Done" actions={[]} onclose={() => (appearance = false)}>
      <Appearance />
      <div class="acts"><HaloButton label="Done" size="small" onpress={() => (appearance = false)} /></div>
    </Dialog>
  {/if}
  {#if launching}
    <LaunchSheet task={launchTask} onclose={() => (launching = false)} />
  {/if}
{/if}

