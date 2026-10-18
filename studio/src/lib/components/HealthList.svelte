<script lang="ts">
  // Findings, worst first, each with its severity as a word and a glyph and
  // an edge in its colour, so colour never carries it alone. What can be put
  // right says so; the page puts the fix beside the list.
  import Glyph from './Glyph.svelte';
  import { severityOf, summaryOf, worstFirst, type Finding } from '../health';

  interface Props {
    findings: Finding[];
    /** The audit's own verdict, when it gives one. */
    verdict?: string;
    /** The command the list comes from. */
    from?: string;
    noneText?: string;
  }

  let { findings, verdict = undefined, from = undefined, noneText = 'Nothing to put right.' }: Props = $props();

  const sorted = $derived(worstFirst(findings));
  const summary = $derived(summaryOf(findings));
</script>

<div class="ls-health">
  {#if verdict || summary}
    <p class="sum">{#if verdict}The audit says <b>{verdict.toLowerCase()}</b>. {/if}{summary}</p>
  {/if}
  {#if !sorted.length}
    <p class="none">✓ {noneText}</p>
  {:else}
    <ul>
      {#each sorted as finding, index (index)}
        {@const severity = severityOf(finding.severity)}
        <li class={severity}>
          <Glyph name={severity} cls="g" />
          <span class="n"><span>{severity === 'ok' ? 'fine' : severity}</span>{finding.name ?? ''}</span>
          {#if finding.detail}<span class="d">{finding.detail}</span>{/if}
          {#if finding.fixable}<span class="ls-tag accent"><Glyph name="fix" />can be put right</span>{/if}
        </li>
      {/each}
    </ul>
  {/if}
  {#if from}<p class="ls-from">From <code>{from}</code>.</p>{/if}
</div>
