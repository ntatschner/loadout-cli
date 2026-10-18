<script lang="ts">
  // Context in Minimal: what a session on the project starts with, against
  // its budget, then the documents' and the health audit's findings as
  // counts with an example. Opening one shows that section.
  import { untrack } from 'svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import Context from '../Context.svelte';
  import { readJson } from '../../lib/api';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  let slug = $state<string | null>(null);
  let context = $state<{ estimatedTokens: number; tokenBudget: number; overBudget: boolean; nearBudget: boolean } | null>(null);
  let docs = $state<{ path?: string; line?: number; kind?: string; detail?: string }[] | null>(null);
  let audit = $state<{ specialist?: string; rule?: string }[] | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  async function load() {
    // The project the page is on, or the first, as the Context page chooses.
    const project = rest[0] ?? (await readJson<{ projects: { id: string }[] }>('project list').then((read) => read.projects[0]?.id ?? null, () => null));

    if (!project) {
      return;
    }

    slug = project;
    readJson<{ context: NonNullable<typeof context> }>('instructions explain', ['--project', project, '--mode', 'implement']).then((read) => (context = read.context), () => undefined);
    readJson<{ findings: NonNullable<typeof docs> }>('docs audit', [project]).then((read) => (docs = read.findings), () => undefined);
    readJson<{ findings: NonNullable<typeof audit> }>('instructions audit', ['--project', project]).then((read) => (audit = read.findings), () => undefined);
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

  const figure = (n: number) => n.toLocaleString('en-GB');
  const items: QuietItem[] = $derived([
    ...(docs?.length
      ? [{ id: 'docs', kind: 'documents', title: `${docs.length} place${docs.length === 1 ? '' : 's'} where the documents have come adrift`, word: 'to put right', where: slug, ask: [docs[0].path ? `${docs[0].path}${docs[0].line ? `:${docs[0].line}` : ''}` : null, docs[0].detail].filter(Boolean).join(': ') }]
      : []),
    ...(audit?.length
      ? [{ id: 'health', kind: 'health', title: `${audit.length} place${audit.length === 1 ? '' : 's'} where the project departs from what its specialists ask`, word: 'to put right', where: slug, ask: [audit[0].specialist, audit[0].rule].filter(Boolean).join(': ') }]
      : []),
  ]);
</script>

{#snippet sentence()}
  {#if context}
    A session on {slug} starts with <b>{figure(context.estimatedTokens)} tokens</b>, {context.overBudget ? `${figure(context.estimatedTokens - context.tokenBudget)} over` : context.nearBudget ? 'near' : 'within'} its budget of {figure(context.tokenBudget)}.
  {/if}
{/snippet}

{#snippet everything()}<Context {rest} />{/snippet}

<QuietPage title="Context" sentence={context ? sentence : undefined} {items} open={rest.length > 0} onopen={(item, from) => (open = { item, from })} {everything} calmText="Its documents and its specialists' checks find nothing to put right." />

{#if open && slug}
  <FocusFrame title={open.item.title} sub={slug} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    <Context rest={[slug, open.item.id]} focused />
  </FocusFrame>
{/if}
