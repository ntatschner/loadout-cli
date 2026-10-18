<script lang="ts">
  // A team you can run, or a template whose shape you copy. Anything that
  // stops it running here is said on the card, and Run it says why it waits.
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';

  interface Props {
    name: string;
    description: string;
    nodes?: number;
    lead?: string | null;
    posture?: string | null;
    budget?: string | null;
    doneWhen?: number;
    origin?: string | null;
    template?: boolean;
    problems?: string[];
    href?: string;
    onrun?: () => void;
    oncopy?: () => void;
  }

  let { name, description, nodes = 0, lead = null, posture = null, budget = null, doneWhen = 0, origin = null, template = false, problems = [], href = undefined, onrun = undefined, oncopy = undefined }: Props = $props();

  const facts = $derived(
    [nodes ? `${nodes} nodes` : null, lead ? `led by ${lead}` : null, template ? null : posture, template ? null : budget, doneWhen ? `${doneWhen} done-when` : null]
      .filter(Boolean)
      .join(' · '),
  );
</script>

<article class="ls-teamcard" class:tpl={template} aria-label={name}>
  <div class="top">
    <h3>{#if href}<a {href}>{name}</a>{:else}{name}{/if}</h3>
    <span class="ls-chip">{template ? 'a shape to copy' : (origin ?? 'ships with Loadout')}</span>
  </div>
  <p>{description}</p>
  <span class="facts">{facts}</span>
  {#each problems as problem, index (index)}
    <span class="prob"><Signal word="blocked" /> Can’t run here: {problem}</span>
  {/each}
  <div class="ls-answers">
    {#if template}
      <HaloButton label="Make a team from it" size="small" cmd={`opens Make a team · from ${name}`} onpress={() => oncopy?.()} />
    {:else}
      <HaloButton label="Run it" size="small" cmd="opens Start a run · starts nothing yet" disabledReason={problems.length ? 'can’t run here until that is fixed' : undefined} onpress={() => onrun?.()} />
    {/if}
  </div>
</article>
