<script lang="ts">
  // Past conversations from both agents, each with what it was launched for
  // and whether it is running, and the way to carry it on. Shared by Sessions,
  // which owns the history, and a project's page, which shows its own.
  import HaloButton from './HaloButton.svelte';
  import type { Conversation } from '../api';
  import { carriedWords, hostedIn, resumeWords } from '../resume';
  import { reveal } from '../reveal.svelte';
  import { studio } from '../state.svelte';

  interface Props {
    conversations: Conversation[];
    label: string;
    /** Opens the launch sheet filled in with what would carry over. */
    onchanges?: (conversation: Conversation) => void;
    /** Leaves the project out of each row, where the page is already about one. */
    inProject?: boolean;
  }

  let { conversations, label, onchanges = undefined, inProject = false }: Props = $props();

  let busy = $state<string | null>(null);

  const unable = $derived(
    studio.watchOnly
      ? studio.watchOnlyReason
      : studio.about?.startsSessions === false
        ? 'This daemon cannot open a terminal here'
        : undefined,
  );

  function when(at: string): string {
    const minutes = Math.max(0, Math.round((studio.now - Date.parse(at)) / 60000));

    return minutes < 1 ? 'just now' : minutes < 60 ? `${minutes} min ago` : minutes < 60 * 24 ? `${Math.round(minutes / 60)} h ago` : `${Math.round(minutes / 1440)} d ago`;
  }

  function where(conversation: Conversation): string {
    const parts = [
      inProject ? null : (conversation.project ?? `not a project · ${conversation.directory}`),
      conversation.agent,
      conversation.branch,
      when(conversation.lastActive),
    ];

    return parts.filter(Boolean).join(' · ');
  }

  function running(conversation: Conversation): string | null {
    return hostedIn(conversation.running) ? 'open in Studio' : conversation.running === 'elsewhere' ? 'looks to be running elsewhere' : null;
  }

  async function resume(conversation: Conversation) {
    busy = conversation.id;

    try {
      const id = await studio.resume(conversation);

      if (id) {
        reveal({ kind: 'session', id, launched: true });
      }
    } finally {
      busy = null;
    }
  }
</script>

{#if conversations.length}
  <ul class="slist convs" aria-label={label}>
    {#each conversations as conversation (`${conversation.agent}:${conversation.id}`)}
      {@const there = hostedIn(conversation.running)}
      <li class="conv">
        <div class="what">
          <span class="t">{conversation.title}</span>
          <span class="x">{where(conversation)}</span>
          <span class="x carried">{carriedWords(conversation)}{running(conversation) ? ` · ${running(conversation)}` : ''}</span>
        </div>
        <div class="acts">
          {#if there}
            <HaloButton label="Open" size="small" cmd="goes to its terminal" onpress={() => reveal({ kind: 'session', id: there })} />
          {:else}
            <HaloButton
              label={conversation.registered ? 'Resume' : 'Register and resume'}
              size="small"
              cmd={busy === conversation.id ? 'starting…' : resumeWords(conversation)}
              disabledReason={unable ?? (busy ? 'one at a time' : undefined)}
              onpress={() => void resume(conversation)}
            />
            {#if onchanges && conversation.registered}
              <button type="button" class="linkish" disabled={!!unable} title={unable} onclick={() => onchanges?.(conversation)}>Resume with changes…</button>
            {/if}
          {/if}
        </div>
      </li>
    {/each}
  </ul>
{:else}
  <p class="calm">No conversation from either agent is on record here yet.</p>
{/if}

<style>
  .conv { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 4px 14px; align-items: center; padding: 8px 12px; }
  .what { display: grid; gap: 2px; min-width: 0; }
  .what .t { font-size: 15px; color: var(--ink-strong); overflow-wrap: anywhere; }
  .what .x { font-family: var(--font-mono); font-size: 11.5px; color: var(--muted); overflow-wrap: anywhere; }
  .acts { display: grid; justify-items: end; gap: 4px; }
  .linkish { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; font-size: 12.5px; cursor: pointer; text-decoration: underline; text-underline-offset: 3px; }
  .linkish:disabled { color: var(--muted); cursor: default; text-decoration: none; }
  .linkish:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
  @media (max-width: 600px) {
    .conv { grid-template-columns: 1fr; }
    .acts { justify-items: start; }
  }
</style>
