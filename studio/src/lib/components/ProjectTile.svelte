<script lang="ts">
  // One project on the Projects wall: name, slug and path, its agent, and
  // what matters at a glance: tasks doing and blocked, findings to put
  // right, ideas. A count not read yet is left out, never shown as nought.
  // With place, a hollow tile for a way to add one.
  interface Props {
    project?: { id: string; name: string; available?: boolean; path?: string; defaultAgent?: string; pinned?: boolean; branch?: string | null; clean?: boolean | null };
    href: string;
    picked?: boolean;
    doing?: number;
    blocked?: number;
    findings?: number;
    ideas?: number;
    place?: string;
    sub?: string;
  }

  let { project = undefined, href, picked = false, doing = undefined, blocked = undefined, findings = undefined, ideas = undefined, place = undefined, sub = undefined }: Props = $props();

  const away = $derived(project?.available === false);
</script>

{#if place}
  <a class="ls-ptile place" {href} aria-current={picked ? 'page' : undefined}><span class="nm">{place}</span>{#if sub}<span class="slug">{sub}</span>{/if}</a>
{:else if project}
  <a class={['ls-ptile', away ? 'away' : ''].filter(Boolean).join(' ')} {href} aria-current={picked ? 'page' : undefined}>
    <span class="nm">{project.name || project.id}{#if project.pinned}<span class="pin">pinned</span>{/if}</span>
    <span class="slug">{project.id}{away ? '' : ` · ${project.path ?? ''}`}</span>
    <span class="facts">
      {#if away}
        <span class="ls-tag dashed">not on this machine</span>
      {:else if project.branch}
        <span class={`ls-tag ${project.clean === false ? 'needs' : ''}`}>{project.branch}{project.clean === false ? ' · uncommitted' : ' · clean'}</span>
      {/if}
      <span class="ls-tag">{project.defaultAgent || 'claude'}</span>
    </span>
    {#if doing !== undefined || blocked || findings !== undefined || ideas}
      <span class="counts">
        {#if doing !== undefined}<span><b>{doing}</b> doing</span>{/if}
        {#if blocked}<span class="needs"><b>{blocked}</b> blocked</span>{/if}
        {#if findings !== undefined}<span class={findings ? 'needs' : ''}>{#if findings}<b>{findings}</b> to put right{:else}✓ healthy{/if}</span>{/if}
        {#if ideas}<span><b>{ideas}</b> idea{ideas === 1 ? '' : 's'}</span>{/if}
      </span>
    {/if}
  </a>
{/if}
