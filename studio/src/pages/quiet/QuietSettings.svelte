<script lang="ts">
  // Settings in Minimal: how the daemon stands and how much on this machine
  // can be put right, then doctor's fixable findings, packs fetched but not
  // approved here, and held schedules. Opening one shows its section.
  import { untrack } from 'svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import Settings from '../Settings.svelte';
  import { readJson, teamApi } from '../../lib/api';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  let paused = $state<boolean | null>(null);
  let autostart = $state<{ enabled?: boolean } | null>(null);
  let checks = $state<{ name: string; detail: string; fixable?: boolean }[] | null>(null);
  let packs = $state<{ name: string; approved?: boolean }[] | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  function load() {
    teamApi.daemon().then((read) => (paused = read.paused), () => undefined);
    readJson<{ enabled?: boolean }>('daemon autostart show').then((read) => (autostart = read), () => undefined);
    readJson<{ checks: NonNullable<typeof checks> }>('doctor').then((read) => (checks = read.checks), () => undefined);
    readJson<NonNullable<typeof packs>>('pack list').then((read) => (packs = read), () => undefined);
  }

  $effect(() => untrack(load));

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(load);
    }
  });

  const fixable = $derived((checks ?? []).filter((one) => one.fixable));
  const items: QuietItem[] = $derived([
    ...fixable.map((one) => ({ id: `machine:${one.name}`, kind: 'this machine', title: one.name, word: 'to put right', where: 'loadout doctor', ask: one.detail })),
    ...(packs ?? []).filter((one) => !one.approved).map((one) => ({ id: `agents:${one.name}`, kind: 'pack', title: one.name, word: 'not approved', where: 'fetched, not approved here' })),
    ...(paused ? [{ id: 'daemon:held', kind: 'the daemon', title: "The daemon's schedules are held", word: 'held', where: 'nothing scheduled fires' }] : []),
  ]);
</script>

{#snippet sentence()}
  The daemon is <b>{paused ? 'running, with its schedules held' : 'running'}</b>{#if autostart} and {autostart.enabled ? 'starts when you log in' : 'does not start when you log in'}{/if}.{#if fixable.length} <b>{fixable.length} thing{fixable.length === 1 ? '' : 's'}</b> on this machine can be put right.{/if}
{/snippet}

{#snippet everything()}<Settings {rest} />{/snippet}

<QuietPage title="Settings" sentence={checks || paused !== null ? sentence : undefined} {items} open={rest.length > 0} onopen={(item, from) => (open = { item, from })} {everything} calmText="Nothing on this machine needs putting right." />

{#if open}
  <FocusFrame title={open.item.title} sub={open.item.where ?? undefined} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    <Settings rest={[open.item.id.split(':')[0]]} focused />
  </FocusFrame>
{/if}
