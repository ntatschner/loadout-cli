<script lang="ts">
  // Ambient: Home for a screen across the room. The key numbers, the halo
  // on whatever most needs you, and everything else circling it. Escape or
  // Leave ambient leaves; N starts a session. Fullscreen is this browser's
  // choice and is remembered here.
  import { onMount, tick } from 'svelte';
  import Halo from '../lib/components/Halo.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Switch from '../lib/components/Switch.svelte';
  import Core from './Core.svelte';
  import { rankUrgent, wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';
  import type { Orbiter } from '../lib/types';
  import { actionsFor } from '../lib/actions';
  import { revealParts } from '../lib/reveal.svelte';

  let { onleave, onlaunch }: { onleave: () => void; onlaunch: () => void } = $props();

  let focusId = $state<string | null>(null);
  let region = $state<HTMLDivElement | undefined>();
  let big = $state(typeof window !== 'undefined' && window.innerWidth >= 900);

  const lines = $derived(studio.lines);
  const focus = $derived(lines.find((line) => line.id === focusId) ?? rankUrgent(lines)[0] ?? lines[0]);
  const waiting = $derived(studio.waiting);
  const actions = $derived(focus ? actionsFor(studio, focus, revealParts) : []);
  const running = $derived(lines.filter((line) => line.stage === 2 && !line.waiting && !line.blocked).length);
  const oldest = $derived.by(() => {
    const asked = waiting.map((line) => line.askedAt).filter((at): at is number => at !== undefined);

    return asked.length ? `${Math.max(1, Math.round(studio.now / 60000) - Math.min(...asked))} min` : 'none';
  });

  const orbiters: Orbiter[] = $derived(
    big && focus
      ? lines.filter((line) => line.id !== focus.id).slice(0, 8).map((line) => ({ key: line.id, title: line.title, word: wordFor(line, studio.idleAfter), onpick: () => (focusId = line.id) }))
      : [],
  );

  async function fullscreen(on: boolean) {
    studio.setFullscreen(on);

    try {
      if (on && !document.fullscreenElement) {
        await document.documentElement.requestFullscreen?.();
      } else if (!on && document.fullscreenElement) {
        await document.exitFullscreen?.();
      }
    } catch {
      // The browser refused, as it may without a press to answer: the
      // choice is kept for next time and Ambient carries on in the window.
    }
  }

  onMount(() => {
    document.body.classList.add('ambient-open');
    tick().then(() => region?.querySelector<HTMLElement>('button')?.focus());

    if (studio.fullscreen) {
      void fullscreen(true);
    }

    return () => {
      document.body.classList.remove('ambient-open');

      if (document.fullscreenElement) {
        void document.exitFullscreen?.().catch(() => undefined);
      }
    };
  });

  function keys(event: KeyboardEvent) {
    if (event.key === 'Escape' && !document.querySelector('.ls-dialog')) {
      event.preventDefault();
      onleave();
    }
  }
</script>

<svelte:window onkeydown={keys} onresize={() => (big = window.innerWidth >= 900)} />

<div bind:this={region} class="amb" role="region" aria-label="Ambient">
  <div class="bar">
    <div class="kpis" role="group" aria-label="Key numbers">
      <div><b>{running}</b><span>Running</span></div>
      <div class:warm={waiting.length > 0}><b>{waiting.length}</b><span>Waiting for you</span></div>
      <div class:warm={waiting.length > 0}><b>{oldest}</b><span>Oldest question</span></div>
      <div><b>{lines.length}</b><span>In view</span></div>
    </div>
    <div class="ls-answers flush">
      <HaloButton label="New session" size="small" onpress={onlaunch} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} />
      <HaloButton label="Leave ambient" size="small" onpress={onleave} />
    </div>
  </div>
  <div class="ambswitch">
    <Switch label="Fullscreen" sub="kept in this browser" checked={studio.fullscreen} onchange={(on) => void fullscreen(on)} />
  </div>
  {#if focus}
    <div class="stage" class:roomy={big}>
      <Halo style={studio.prefs?.halo ?? 'orbit'} stage={focus.waiting ? 3 : focus.stage} hour={((focus.mins ?? 0) % 60) / 60} spend={focus.cap ? (focus.cost ?? 0) / focus.cap : null} {orbiters} orbitRadius={330}>
        {#snippet core()}<Core line={focus} answers={false} />{/snippet}
      </Halo>
    </div>
    {#if actions.length}
      <div class="ls-answers corecmds">
        {#each actions as action (action.label)}<HaloButton size="small" {...action} />{/each}
      </div>
    {/if}
  {:else}
    <p class="calm">Nothing is in flight.</p>
  {/if}
  <div class="strip" aria-hidden="true">
    {#each studio.feed.slice(0, 3) as entry, i (i)}<div><span class="tm">{entry.t}</span>{entry.text}</div>{/each}
  </div>
</div>
