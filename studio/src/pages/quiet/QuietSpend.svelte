<script lang="ts">
  // Spend in Minimal: what the last 30 days cost, billed as input, and what
  // the cache saved; then what is running now. Opening one shows the
  // session's terminal where Studio hosts it, as Minimal's Home does.
  import { untrack } from 'svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import Spend from '../Spend.svelte';
  import { readJson } from '../../lib/api';
  import { tokens } from '../../lib/glyphs';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';

  interface Running {
    launchId: string;
    project: string;
    agent: string;
    elapsedSeconds: number;
    state: string;
  }

  let totals = $state<{ billedInputEquivalent: number; savedFraction: number } | null>(null);
  let running = $state<Running[] | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  function load() {
    readJson<{ totals: NonNullable<typeof totals> }>('usage', ['--by', 'project', '--days', '30']).then((read) => {
      totals = read.totals;
      studio.spentMonth = read.totals.billedInputEquivalent;
    }, () => undefined);
    readJson<Running[]>('running').then((read) => (running = read), () => undefined);
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

  const minutes = (seconds: number) => (seconds < 120 ? `${seconds}s` : `${Math.round(seconds / 60)} min`);
  const items: QuietItem[] = $derived((running ?? []).map((one) => ({ id: one.launchId, kind: 'session', title: one.project, word: one.state, where: `${one.agent} · ${minutes(one.elapsedSeconds)}` })));
  // A session Studio hosts on the same project, whose terminal can be shown here.
  const hosted = (project: string) => studio.lines.find((line) => line.kind === 'session' && line.stage !== 4 && line.session?.project === project) ?? null;
</script>

{#snippet sentence()}
  {#if totals}<b>{tokens(totals.billedInputEquivalent)} tokens</b> billed as input in the last 30 days; the cache saved <b>{Math.round(totals.savedFraction * 100)}%</b>.{/if}
{/snippet}

{#snippet everything()}<Spend />{/snippet}

<QuietPage title="Spend" sentence={totals ? sentence : undefined} {items} open={false} onopen={(item, from) => (open = { item, from })} {everything} calmText="Nothing is running." />

{#if open}
  {@const line = hosted(open.item.title)}
  <FocusFrame title={open.item.title} sub={open.item.where ?? undefined} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    {#if line}
      {#await import('../../lib/components/SessionPane.svelte')}
        <p class="calm" role="status">Opening the terminal…</p>
      {:then pane}
        <pane.default id={line.ref} onready={(handle) => handle?.focus()} />
      {/await}
    {:else}
      <p class="calm">It runs in a terminal of its own, outside Studio, so its terminal cannot be shown here. Studio shows what it spent once its transcript is read.</p>
    {/if}
  </FocusFrame>
{/if}
