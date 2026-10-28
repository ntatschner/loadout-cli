<script lang="ts">
  // Home: everything in flight, as the Lens (the halo, what needs you and the
  // rest) or as the Map (every piece of work a line through its stages).
  // Both layouts are edited in place and kept in Loadout's config.
  import Halo from '../lib/components/Halo.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Meter from '../lib/components/Meter.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import StartRail from '../lib/components/StartRail.svelte';
  import TubeMap from '../lib/components/TubeMap.svelte';
  import LineStrip from '../lib/components/LineStrip.svelte';
  import NetworkStatus from '../lib/components/NetworkStatus.svelte';
  import MapFilterBar from '../lib/components/MapFilterBar.svelte';
  import Core from './Core.svelte';
  import Panel from './Panel.svelte';
  import { actionsFor } from '../lib/actions';
  import { command } from '../lib/commands';
  import { LENS_PANELS, MAP_PANELS, PANEL_NAMES, hide, move, place, readLayout, saved, show, type Layout } from '../lib/layout';
  import { clockOf, hiddenOf, type Work } from '../lib/lines';
  import { PRESETS, filterLines, filterOf, hiddenNote, keysOf, presetOf, projectsIn, type MapFilter } from '../lib/mapFilter';
  import { router } from '../lib/router.svelte';
  import { reveal, revealParts } from '../lib/reveal.svelte';
  import { rankUrgent, wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';
  import { urgencyClass } from '../lib/urgency';
  import { css } from '../lib/css';

  let { onambient, onlaunch }: { onambient: () => void; onlaunch: (task?: string) => void } = $props();

  let view = $state<'lens' | 'map'>('lens');
  let editing = $state(false);
  let narrow = $state(typeof window !== 'undefined' && window.innerWidth < 600);
  let focusId = $state<string | null>(router.route.rest[0] ?? null);
  let picked = $state<string | undefined>(undefined);
  let startTask = $state('');
  let dragging = $state<string | null>(null);

  $effect(() => {
    // A palette row or a link that names a line focuses it.
    const named = router.route.page === 'home' ? router.route.rest[0] : undefined;

    if (named) {
      focusId = named;
      view = 'lens';
    }
  });

  const lines = $derived(studio.lines);

  // The filters narrow the Map and the Lens alike; the header's count, the
  // network status and the spend keep the lot. Chosen here at once, and kept
  // as studio-map-kinds, studio-map-states and studio-map-projects.
  let filterChosen = $state<MapFilter | null>(null);
  const filter = $derived(filterChosen ?? filterOf(studio.prefs));
  const filtered = $derived(filterLines(lines, filter, studio.now));
  const drawn = $derived(filtered.drawn);
  const note = $derived(hiddenNote(filtered.hidden));
  const hidden = $derived(hiddenOf(lines, drawn));
  const waitingHidden = $derived(filtered.hidden.filter((line) => line.waiting).length);
  const pickedLine = $derived(drawn.find((line) => line.id === picked) ?? drawn[0]);

  // A line asked for by name is shown even when the filters hide it, with
  // the note: clearing filters somebody saved, unasked, would be a surprise.
  const named = $derived(lines.find((line) => line.id === focusId));
  const ranked = $derived(rankUrgent(drawn));
  const focus = $derived(named ?? ranked[0] ?? drawn[0]);
  const focusHidden = $derived(!!focus && !drawn.some((line) => line.id === focus.id));
  const index = $derived(focus ? drawn.findIndex((line) => line.id === focus.id) : -1);

  /** Writes what changed. In flight is the three keys unset, as a fresh config has them. */
  function filterTo(next: MapFilter) {
    const was = keysOf(filter);
    const inflight = presetOf(next) === 'inflight';

    filterChosen = next;

    keysOf(next).forEach(([key, value], i) => {
      if (inflight || value !== was[i][1]) {
        void studio.setPreference(key, inflight ? '' : value);
      }
    });
  }

  const everything = (event: Event) => {
    event.preventDefault();
    filterTo(PRESETS.all);
  };

  const lens: Layout = $derived(readLayout(studio.prefs?.layoutLens, LENS_PANELS));
  const map: Layout = $derived(readLayout(studio.prefs?.layoutMap, MAP_PANELS));
  const layout = $derived(view === 'lens' ? lens : map);
  const panels = $derived(view === 'lens' ? LENS_PANELS : MAP_PANELS);
  const key = $derived(view === 'lens' ? 'studio-layout-lens' : 'studio-layout-map');

  function keep(next: Layout) {
    void studio.setPreference(key, saved(next, panels));
  }

  const waitingOn = $derived(studio.waiting.length);
  const halo = $derived(narrow ? 'orbit' : (studio.prefs?.halo ?? 'orbit'));

  const spent = $derived(lines.filter((line) => line.kind === 'team run' && line.cap).reduce((sum, line) => sum + (line.cost ?? 0), 0));
  const capped = $derived(lines.filter((line) => line.kind === 'team run' && line.cap).reduce((sum, line) => sum + (line.cap ?? 0), 0));

  const oldest = $derived.by(() => {
    const asked = studio.waiting.filter((line) => line.askedAt);

    if (!asked.length) {
      return 'none';
    }

    const first = asked.reduce((a, b) => ((a.askedAt ?? 0) < (b.askedAt ?? 0) ? a : b));

    return `${Math.max(1, Math.round(studio.now / 60000) - (first.askedAt ?? 0))} min · ${first.where}`;
  });

  const startProject = $derived(focus?.project ?? studio.lines.find((line) => line.project)?.project ?? null);

  function factsOf(line: Work) {
    const facts = [{ k: 'Kind', v: `${line.kind} · ${line.where}` }];

    if (line.session?.agent) {
      facts.push({ k: 'Agent', v: `${line.session.agent}${line.session.mode ? ` · ${line.session.mode}` : ''}` });
    }

    if (line.mins) {
      facts.push({ k: 'Running', v: `${line.mins} min` });
    }

    if (line.kind === 'session') {
      facts.push({ k: 'Quiet', v: `${line.quiet ?? 0} min` });
    }

    if (line.cap) {
      facts.push({ k: 'Cost', v: `$${(line.cost ?? 0).toFixed(2)} of $${line.cap.toFixed(2)} budget` });
    } else if (line.cost) {
      facts.push({ k: 'Cost', v: `$${line.cost.toFixed(2)}, no budget set` });
    }

    return facts;
  }

  function resized() {
    narrow = window.innerWidth < 600;
  }

  function step(by: number) {
    if (drawn.length) {
      focusId = drawn[(index + by + drawn.length) % drawn.length].id;
    }
  }
</script>

<svelte:window onresize={resized} />

<div class="head">
  <div>
    <h1>Home</h1>
    <span class="fact" class:need={waitingOn > 0}>{waitingOn ? `${waitingOn} waiting on you` : 'all calm'}</span>
  </div>
  <div class="views" role="group" aria-label="Home view">
    <button type="button" aria-pressed={view === 'lens' ? 'true' : 'false'} onclick={() => (view = 'lens')}>Lens</button>
    <button type="button" aria-pressed={view === 'map' ? 'true' : 'false'} onclick={() => (view = 'map')}>Map</button>
  </div>
  <span class="grow"></span>
  <span class="clock">{clockOf(new Date(studio.now).toISOString())}</span>
  <HaloButton label="Ambient" size="small" onpress={onambient} />
  <HaloButton
    label={editing ? 'Done' : 'Edit layout'}
    size="small"
    onpress={() => (editing = !editing)}
    disabledReason={studio.watchOnly ? `Studio is only watching · $ loadout config set ${key} …` : undefined}
  />
</div>

{#if editing}
  <div class="editbar" role="region" aria-label="Editing the layout">
    <p>Editing the {view === 'lens' ? 'Lens' : 'Map'} layout. Drag a panel by its name, or use its arrows.</p>
    {#each layout.hidden as panel (panel)}
      <HaloButton label={`Add back ${PANEL_NAMES[panel]}`} size="small" onpress={() => keep(show(layout, panel))} />
    {/each}
    <HaloButton label="Reset" size="small" cmd={command.setting(key, '')} onpress={() => void studio.setPreference(key, '')} />
    <HaloButton label="Done" size="small" solid onpress={() => (editing = false)} />
  </div>
{/if}

{#snippet hiddenLink()}
  <p class="calm small">{note}. <a href="#home" onclick={everything}>Show everything</a></p>
{/snippet}

{#if lines.length}
  <MapFilterBar {filter} projects={projectsIn(lines, filter.projects)} onchange={filterTo} />
{/if}

{#if !lines.length}
  <div class="placeholder">
    <p class="calm">Nothing is in flight: no session, team run, idea being shaped or task being done. Start a session from here, or from a terminal.</p>
    <HaloButton label="New session…" cmd="opens the launch sheet · changes nothing yet" onpress={() => onlaunch()} disabledReason={studio.watchOnly ? 'Studio is only watching · $ loadout launch <project>' : undefined} />
  </div>
{:else}
  <div class={view === 'lens' ? 'lens' : 'mapview'} class:editing>
    {#each layout.order as panel, position (panel)}
      {#if !layout.hidden.includes(panel)}
        <Panel
          id={panel}
          name={PANEL_NAMES[panel]}
          wide={view === 'map' || panel === 'start' || panel === 'signal'}
          quiet={view === 'map'}
          {editing}
          first={position === 0}
          last={position === layout.order.length - 1}
          dragging={dragging === panel}
          onmove={(by) => keep(move(layout, panel, by))}
          onhide={() => keep(hide(layout, panel))}
          ondragstart={() => (dragging = panel)}
          ondrop={() => {
            if (dragging) {
              keep(place(layout, dragging, panel));
            }

            dragging = null;
          }}
          number={view === 'lens' ? `0${LENS_PANELS.indexOf(panel as never) + 1}` : ''}
          count={panel === 'needs' ? ranked.length : panel === 'all' ? drawn.length : undefined}
        >
          {#if panel === 'focus' && focus}
            <div class="halo-col">
              {#if focusHidden}
                <p class="calm small">The filters hide this one; it is shown because it was asked for. {note}. <a href="#home" onclick={everything}>Show everything</a></p>
              {/if}
              {#if editing}
                <div class="halostyle" role="radiogroup" aria-label="Halo style">
                  {#each ['orbit', 'gyroscope'] as style (style)}
                    <button type="button" role="radio" aria-checked={halo === style ? 'true' : 'false'} onclick={() => void studio.setPreference('studio-halo', style)}>{style === 'orbit' ? 'Orbit' : 'Gyroscope'}</button>
                  {/each}
                  <code class="ls-cmd">{command.setting('studio-halo', halo)}</code>
                </div>
              {/if}
              <Halo
                style={halo}
                small={narrow}
                stage={focus.waiting ? 3 : focus.stage}
                hour={((focus.mins ?? 0) % 60) / 60}
                spend={focus.cap ? (focus.cost ?? 0) / focus.cap : null}
              >
                {#snippet core()}
                  {#if narrow}<Signal word={wordFor(focus, studio.idleAfter)} big />{:else}<Core line={focus} answers={false} />{/if}
                {/snippet}
              </Halo>
              {#if narrow}
                <div class="undercore"><Core line={focus} /></div>
              {:else if actionsFor(studio, focus, revealParts).length}
                <div class="ls-answers corecmds">
                  {#each actionsFor(studio, focus, revealParts) as action (action.label)}
                    <HaloButton size="small" {...action} />
                  {/each}
                </div>
              {/if}
              <div class="stepper">
                <HaloButton label="Prev" size="small" onpress={() => step(-1)} />
                {index < 0 ? '–' : index + 1} of {drawn.length}
                <HaloButton label="Next" size="small" onpress={() => step(1)} />
              </div>
            </div>
          {:else if panel === 'needs'}
            {#if note}{@render hiddenLink()}{/if}
            {#if !ranked.length}
              <p class="calm">Nothing needs you.</p>
            {:else}
              {#each ranked as line (line.id)}
                <div class="waitcard" class:problem={line.problem && !line.waiting}>
                  <span class="w">{line.kind} · {line.where}{line.askedAt ? ` · ${Math.max(1, Math.round(studio.now / 60000) - line.askedAt)} min` : ''}</span>
                  <p class="q">{line.waiting ? (line.ask ?? line.title) : `${line.title} · ${line.failed}`}</p>
                  <div class="ls-answers flush">
                    {#each actionsFor(studio, line, revealParts) as action (action.label)}
                      <HaloButton size="small" {...action} />
                    {/each}
                    <HaloButton label="Focus it" size="small" onpress={() => (focusId = line.id)} />
                  </div>
                </div>
              {/each}
            {/if}
          {:else if panel === 'all'}
            <ul class="rows" aria-label="Everything">
              {#each drawn as line (line.id)}
                {@const top = ranked[0]}
                <li>
                  <button
                    type="button"
                    class={`row ${urgencyClass(line.id === top?.id ? 0 : 1, top && !editing ? 0 : -1, top?.problem && !top?.waiting ? 'problem' : 'needs')}`}
                    aria-current={line.id === focus?.id ? 'true' : 'false'}
                    onclick={() => (focusId = line.id)}
                  ><span class="t">{line.title}</span><Signal word={wordFor(line, studio.idleAfter)} /><span class="x">{line.kind} · {line.where} · {line.blocked ?? line.note ?? ''}</span></button>
                </li>
              {/each}
            </ul>
          {:else if panel === 'start'}
            {#if startProject}
              <StartRail
                bind:value={startTask}
                id="home-task"
                command={(task) => `$ loadout launch ${startProject} --task "${task || '…'}"`}
                ondeploy={(task) => {
                  void studio.startSession({ project: startProject, task }, `$ loadout launch ${startProject} --task "${task}"`).then((started) => {
                    if (started) {
                      startTask = '';
                      reveal({ kind: 'session', id: started.id, launched: true });
                    }
                  });
                }}
                disabledReason={studio.watchOnly ? studio.watchOnlyReason : studio.about?.startsSessions === false ? 'This daemon cannot start sessions here' : undefined}
              />
            {/if}
            <p class="calm small">Want the full sheet, with the task picked from what the project has recorded? <a href="#sessions" onclick={(event) => { event.preventDefault(); onlaunch(startTask); }}>New session</a></p>
          {:else if panel === 'signal'}
            <div class="two">
              <ul class="feed" aria-label="What happened">
                {#each studio.feed.length ? studio.feed : [{ t: clockOf(new Date(studio.now).toISOString()) ?? '', text: `Studio online · ${lines.length} in view`, family: 'rest', glyph: '·' }] as entry, i (i)}
                  <li use:css={{ '--c': `var(--${entry.family})` }}><span class="tm">{entry.t}</span><span class="g" aria-hidden="true">{entry.glyph}</span><span>{entry.text}</span></li>
                {/each}
              </ul>
              <div>
                {#if capped > 0}
                  <Meter
                    label="Team runs' spend against their budgets"
                    value={spent}
                    max={capped}
                    threshold={capped * 0.8}
                    thresholdLabel="80% of their budgets"
                    format={(v) => `$${v.toFixed(2)}`}
                    of={() => `of $${capped.toFixed(2)} across the capped runs`}
                    source="the runs' own budgets · says where you stand, stops nothing"
                  />
                {:else}
                  <p class="calm small">No team run in view has a budget. What sessions cost is under Spend: <code class="ls-cmd">$ loadout usage</code></p>
                {/if}
              </div>
            </div>
          {:else if panel === 'status'}
            <NetworkStatus {lines} clock={clockOf(new Date(studio.now).toISOString())} {oldest} />
          {:else if panel === 'map'}
            {#if drawn.length}
              {#if note}{@render hiddenLink()}{/if}
              <TubeMap lines={drawn} bind:picked idleAfter={studio.idleAfter} />
            {:else}
              <p class="calm">Nothing to draw. {hidden} {lines.length - drawn.length === 1 ? 'is' : 'are'} hidden by the filters{waitingHidden ? `, ${waitingHidden} waiting on you` : ''}. <a href="#home" onclick={everything}>Show everything</a></p>
            {/if}
          {:else if panel === 'strip' && pickedLine}
            <LineStrip
              line={pickedLine}
              facts={factsOf(pickedLine)}
              checks={pickedLine.checks}
              events={pickedLine.events}
              actions={actionsFor(studio, pickedLine, revealParts)}
              idleAfter={studio.idleAfter}
            />
          {/if}
        </Panel>
      {/if}
    {/each}
  </div>
{/if}
