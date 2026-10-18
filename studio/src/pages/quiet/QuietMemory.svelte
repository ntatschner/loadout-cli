<script lang="ts">
  // Memory in Minimal: how many topics, across how many projects, and what
  // the audit of the project says; then the audit's findings and what the
  // agent recorded outside the workspace. A finding about a topic opens the
  // topic; the rest open the health and tools, with the import drawer.
  import { untrack } from 'svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import Memory from '../Memory.svelte';
  import { memory, readJson } from '../../lib/api';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  let slug = $state<string | null>(null);
  let counts = $state<Record<string, number>>({});
  let agent = $state<{ command: string; wouldImport?: string[] } | null>(null);
  let audit = $state<{ verdict: string; findings: { severity: string; kind: string; detail: string; topic?: string }[] } | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  async function load() {
    const all = await memory.projects().catch(() => []);
    const project = rest[0] ?? all[0]?.slug ?? null;

    slug = project;

    // Each project's topics, from the route the page reads them by.
    for (const one of all) {
      memory.topics(one.slug).then((read) => {
        counts = { ...counts, [one.slug]: read.topics.length };

        if (one.slug === project) {
          agent = read.agent;
        }
      }, () => undefined);
    }

    if (project) {
      readJson<NonNullable<typeof audit>>('memory audit', [project]).then((read) => (audit = read), () => undefined);
    }
  }

  $effect(() => untrack(() => void load()));

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(() => void load());
    }
  });

  const topics = $derived(Object.values(counts).reduce((sum, one) => sum + one, 0));
  const withTopics = $derived(Object.values(counts).filter((one) => one > 0).length);
  const items: QuietItem[] = $derived([
    ...(audit?.findings ?? []).map((one, index) => ({ id: one.topic ? `topic:${one.topic}` : `tools:${index}`, kind: one.kind, title: one.topic ?? one.kind, word: 'to put right', where: slug, ask: one.detail })),
    ...(agent?.wouldImport?.length ? [{ id: 'tools:agent', kind: 'outside the workspace', title: `${agent.wouldImport.length} topic${agent.wouldImport.length === 1 ? '' : 's'} the agent recorded outside the workspace`, word: 'open', where: slug, ask: `$ ${agent.command}` }] : []),
  ]);
</script>

{#snippet sentence()}
  <b>{topics} topic{topics === 1 ? '' : 's'}</b> across {withTopics} project{withTopics === 1 ? '' : 's'}.{#if audit && slug} The audit of {slug} says <b>{audit.verdict.toLowerCase()}</b>.{/if}
{/snippet}

{#snippet everything()}<Memory slug={rest[0] ?? null} scope={rest[1] ?? null} name={rest[2] ?? null} />{/snippet}

<QuietPage title="Memory" sentence={Object.keys(counts).length ? sentence : undefined} {items} open={rest.length > 0} onopen={(item, from) => (open = { item, from })} {everything} calmText="The audit finds nothing to put right." />

{#if open && slug}
  <FocusFrame title={open.item.title} sub={slug} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    {#if open.item.id.startsWith('topic:')}
      <Memory {slug} scope="project" name={open.item.id.slice('topic:'.length)} focused="topic" />
    {:else}
      <Memory {slug} scope={null} name={null} focused="tools" />
    {/if}
  </FocusFrame>
{/if}
