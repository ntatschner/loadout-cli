<script lang="ts">
  // Who is on a run: each node's state and what it is doing, its turns and
  // cost, what it was refused, and how long it has been quiet against its own
  // usual turn.
  import Signal from './Signal.svelte';
  import { money } from '../teams';

  interface Member {
    node: string;
    role: string;
    model?: string | null;
    word: string;
    doing?: string | null;
    quiet?: string | null;
    turns: number;
    cost: number;
    refused?: number;
  }

  interface Props {
    nodes: Member[];
    picked?: string | null;
    onpick?: (node: string) => void;
    label?: string;
  }

  let { nodes, picked = null, onpick = undefined, label = 'Who is on it' }: Props = $props();
</script>

{#snippet inner(member: Member)}
  <span class="nm"><b>{member.node}</b><span>{[member.role, member.model].filter(Boolean).join(' · ')}</span></span>
  <span class="doing"><Signal word={member.word} />{member.doing ? ` · ${member.doing}` : ''}{#if member.quiet}<span class="quiet">Quiet for {member.quiet}</span>{/if}</span>
  <span class="num">{member.turns} {member.turns === 1 ? 'turn' : 'turns'} · {money(member.cost)}{#if member.refused}<span class="refused"> · {member.refused} refused</span>{/if}</span>
{/snippet}

<div class="ls-crew">
  <ul aria-label={label}>
    {#each nodes as member (member.node)}
      <li>
        {#if onpick}
          <button type="button" class="row" aria-pressed={picked === member.node ? 'true' : 'false'} onclick={() => onpick?.(member.node)}>{@render inner(member)}</button>
        {:else}
          <div class="row">{@render inner(member)}</div>
        {/if}
      </li>
    {/each}
  </ul>
</div>
