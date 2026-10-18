<script lang="ts">
  // Ideas in Minimal: how many are being shaped and how many wait for your
  // answers, then each idea not yet accepted, a waiting one with its first
  // unanswered question and a failed one saying so. Opening one shows its
  // workbench; Show everything shows the page.
  import { untrack } from 'svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import Ideas from '../Ideas.svelte';
  import { readJson } from '../../lib/api';
  import { stateWord } from '../../lib/ideas';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  interface Listed {
    id: string;
    title: string;
    project?: string | null;
    stage: string;
    unanswered: number;
    running: boolean;
    failure?: { error: string } | null;
  }

  let ideas = $state<Listed[] | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  function load() {
    readJson<{ ideas: Listed[] }>('idea list').then((read) => (ideas = read.ideas), () => undefined);
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

  /** The first question still unanswered, from the ideas Studio already holds. */
  function firstQuestion(one: Listed): string {
    const held = studio.ideas.find((idea) => idea.detail.id === one.id && (idea.detail.project ?? null) === (one.project ?? null));
    const question = held?.detail.questions.find((asked) => !asked.answer)?.question;

    return question ? `${question}${one.unanswered > 1 ? ` (and ${one.unanswered - 1} more)` : ''}` : `${one.unanswered} question${one.unanswered === 1 ? '' : 's'} for you`;
  }

  const shaping = $derived((ideas ?? []).filter((one) => one.stage !== 'accepted'));
  const waiting = $derived(shaping.filter((one) => stateWord(one) === 'waiting for you').length);
  const items: QuietItem[] = $derived(
    shaping.map((one) => ({
      id: `${one.project ?? '~'}/${one.id}`,
      kind: 'idea',
      title: one.title,
      word: stateWord(one),
      where: one.project ?? 'workspace-wide',
      ask: one.failure ? `Its last round failed: ${one.failure.error}` : one.unanswered ? firstQuestion(one) : '',
    })),
  );
</script>

{#snippet sentence()}
  {shaping.length} idea{shaping.length === 1 ? '' : 's'} being shaped{#if waiting}; <b>{waiting} wait{waiting === 1 ? 's' : ''} for your answers</b>{/if}.
{/snippet}

{#snippet everything()}<Ideas {rest} />{/snippet}

<QuietPage title="Ideas" sentence={ideas ? sentence : undefined} {items} open={rest.length > 0} onopen={(item, from) => (open = { item, from })} {everything} calmText="No idea waits for you." />

{#if open}
  {@const [list, id] = open.item.id.split('/')}
  <FocusFrame title={open.item.title} sub={open.item.where ?? undefined} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    <Ideas rest={[list, id]} focused />
  </FocusFrame>
{/if}
