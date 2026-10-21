<script lang="ts">
  // Events: what needed you over the last month, and what you did about it.
  // Read here; acted on where it happened, which each row links to. The
  // filter lives in the address after a '?', in the same form
  // 'loadout events --filter' takes, so a filtered view can be bookmarked.
  import HaloButton from '../lib/components/HaloButton.svelte';
  import { eventlog, SignedOut, type FeedItem, type FeedPage } from '../lib/api';
  import { byDay, DIRECTION_WORDS, DIRECTIONS, formatFilter, LEVEL_WORDS, LEVELS, linkOf, outstanding, parseFilter, resolvedAfter, SINCE, SOURCE_WORDS, SOURCES, type Filter, type Level, type Source } from '../lib/events';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { query }: { query: string } = $props();

  let page = $state<FeedPage | null>(null);
  let problem = $state<string | null>(null);
  /** Projects seen while this page has been open, for the project box to offer. */
  let known = $state<string[]>([]);

  const filter = $derived(parseFilter(query));
  const days = $derived(byDay(page?.items ?? [], new Date(studio.now)));

  // Read again when the filter changes, and when the daemon says the history did.
  $effect(() => {
    const asked = query;
    void studio.eventsChanged;

    eventlog.list(asked).then(
      (read) => {
        // A stale answer to a filter since changed is dropped.
        if (asked !== query) {
          return;
        }

        page = read;
        problem = null;
        known = [...new Set([...known, ...read.items.map((item) => item.event.project).filter((one): one is string => !!one)])].sort();

        // The daemon's spelling is the canonical one; the address takes it.
        if (read.filter !== asked) {
          router.replaceWith(['events'], read.filter);
        }
      },
      (error) => {
        if (error instanceof SignedOut) {
          studio.signedOut = true;
        } else {
          problem = (error as Error).message;
        }
      },
    );
  });

  function change(next: Partial<Filter>) {
    router.replaceWith(['events'], formatFilter({ ...filter, ...next }));
  }

  function toggle<T extends string>(list: T[], one: T): T[] {
    return list.includes(one) ? list.filter((other) => other !== one) : [...list, one];
  }

  let typing: ReturnType<typeof setTimeout> | undefined;

  function search(text: string) {
    clearTimeout(typing);
    typing = setTimeout(() => change({ text }), 250);
  }

  async function markRead(item: FeedItem) {
    if (item.read || studio.watchOnly) {
      return;
    }

    try {
      studio.unreadEvents = (await eventlog.read([item.event.id])).unread;
    } catch {
      // Marked the next time; opening it is what matters.
    }
  }

  async function markAll() {
    try {
      studio.unreadEvents = (await eventlog.readAll()).unread;
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  const time = (at: string) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const MARKS: Record<Level, string> = { needs: '?', problem: '!', done: '✓', info: '·' };
</script>

<div class="head">
  <div>
    <h1>Events</h1>
    <span class="fact" class:need={!!studio.unreadEvents}>
      {studio.unreadEvents ? `${studio.unreadEvents} unread need you or went wrong` : 'what needed you, and what you did'}
    </span>
  </div>
  <span class="grow"></span>
  <HaloButton
    label="Mark all read"
    size="small"
    cmd="$ loadout events --mark-read"
    disabledReason={studio.watchOnly ? studio.watchOnlyReason : !studio.unreadEvents ? 'Nothing is unread' : undefined}
    onpress={markAll}
  />
</div>

<form class="evfilter" aria-label="Filter the events" onsubmit={(event) => event.preventDefault()}>
  <div class="spctl">
    <span class="lab" id="ev-level">Show</span>
    <div class="periodseg" role="group" aria-labelledby="ev-level">
      {#each LEVELS as level (level)}
        <button type="button" aria-pressed={filter.levels.includes(level) ? 'true' : 'false'} onclick={() => change({ levels: toggle<Level>(filter.levels, level) })}>{LEVEL_WORDS[level]}</button>
      {/each}
    </div>
  </div>

  <div class="spctl">
    <span class="lab" id="ev-direction">Which way</span>
    <div class="periodseg" role="radiogroup" aria-labelledby="ev-direction">
      {#each DIRECTIONS as direction (direction)}
        <button type="button" role="radio" aria-checked={filter.direction === direction ? 'true' : 'false'} onclick={() => change({ direction })}>{DIRECTION_WORDS[direction]}</button>
      {/each}
    </div>
  </div>

  <div class="spctl">
    <span class="lab" id="ev-since">Over</span>
    <div class="periodseg" role="radiogroup" aria-labelledby="ev-since">
      {#each SINCE as [value, label] (value)}
        <button type="button" role="radio" aria-checked={filter.since === value ? 'true' : 'false'} onclick={() => change({ since: value })}>{label}</button>
      {/each}
      {#if filter.since && !SINCE.some(([value]) => value === filter.since)}
        <button type="button" role="radio" aria-checked="true">since {filter.since}</button>
      {/if}
    </div>
  </div>

  <div class="spctl">
    <span class="lab" id="ev-source">From</span>
    <div class="periodseg" role="group" aria-labelledby="ev-source">
      {#each SOURCES as source (source)}
        <button type="button" aria-pressed={filter.sources.includes(source) ? 'true' : 'false'} onclick={() => change({ sources: toggle<Source>(filter.sources, source) })}>{SOURCE_WORDS[source]}</button>
      {/each}
    </div>
  </div>

  <div class="evboxes">
    <label>
      <span class="lab">Project</span>
      <input type="text" name="project" list="ev-projects" value={filter.project} placeholder="Every project" autocomplete="off" onchange={(event) => change({ project: event.currentTarget.value })} />
      <datalist id="ev-projects">{#each known as one (one)}<option value={one}></option>{/each}</datalist>
    </label>
    <label>
      <span class="lab">Title has</span>
      <input type="search" name="text" value={filter.text} placeholder="Words in the title" autocomplete="off" oninput={(event) => search(event.currentTarget.value)} />
    </label>
    <label class="evunread">
      <input type="checkbox" name="unread" checked={filter.unread} onchange={(event) => change({ unread: event.currentTarget.checked })} />
      <span>Unread only</span>
    </label>
  </div>
</form>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

{#if !page}
  {#if !problem}<p class="calm" role="status">Reading the history…</p>{/if}
{:else if page.items.length === 0}
  <p class="calm">
    {query ? 'Nothing matches.' : 'Nothing has needed you yet.'}
    The daemon records what needs you while it runs, and keeps it {page.days ? `${page.days} days` : 'until the history reaches its size limit'}.
  </p>
{:else}
  {#each days as { day, items } (day)}
    <section class="pblock evday" aria-labelledby={`ev-${day}`}>
      <h2 class="ls-zone" id={`ev-${day}`}>{day}</h2>
      <ul class="evlist">
        {#each items as item (item.event.id)}
          {@const link = linkOf(item.event)}
          {@const resolved = resolvedAfter(item)}
          <li class="evrow" data-level={item.event.level} class:cleared={!!item.cleared} class:unread={!item.read}>
            <span class="mark" aria-hidden="true">{MARKS[item.event.level]}</span>
            <span class="when">{time(item.event.at)}</span>
            <div class="what">
              <p class="title">
                {#if !item.read}<span class="ls-sr">Unread: </span>{/if}
                <span class="ls-sr">{LEVEL_WORDS[item.event.level]}: </span>
                {item.event.title}
              </p>
              {#if item.event.detail}<p class="detail">{item.event.detail}</p>{/if}
              <p class="meta">
                {#if item.event.project}<span>{item.event.project}</span>{/if}
                {#if item.event.direction === 'by-me'}<span>you</span>{/if}
                {#if resolved}<span class="resolved">{resolved}</span>{:else if outstanding(item)}<span class="open">still open</span>{/if}
              </p>
            </div>
            <span class="acts">
              {#if link}<a href={link} onclick={() => void markRead(item)}>Go to it</a>{/if}
              {#if !item.read && !studio.watchOnly}<button type="button" class="link" onclick={() => void markRead(item)}>Mark read</button>{/if}
            </span>
          </li>
        {/each}
      </ul>
    </section>
  {/each}
  {#if page.matched > page.items.length}<p class="calm">{page.items.length} of {page.matched} shown, newest first. Narrow the filter to see older ones.</p>{/if}
{/if}

<style>
  .evfilter { display: flex; flex-wrap: wrap; gap: 14px 28px; margin-bottom: calc(var(--space-6) * var(--density)); }
  .evfilter :global(.periodseg button[aria-pressed='true']) { background: var(--tint-picked-c); color: var(--ink-strong); box-shadow: inset 0 -2px 0 var(--accent); }
  .evboxes { display: flex; flex-wrap: wrap; align-items: end; gap: 10px 18px; }
  .evboxes label { display: flex; flex-direction: column; gap: 4px; }
  .evboxes .lab { font-size: 12px; font-weight: 500; letter-spacing: .16em; text-transform: uppercase; color: var(--muted); }
  .evboxes input[type='text'], .evboxes input[type='search'] { min-height: 36px; width: 22ch; max-width: 100%; }
  .evboxes .evunread { flex-direction: row; align-items: center; gap: 8px; min-height: 36px; font-size: 13px; color: var(--ink-soft); }
  .evlist { list-style: none; margin: 0; padding: 0; max-width: 76ch; }
  .evrow { display: grid; grid-template-columns: 1.5em 3.6em minmax(0, 1fr) auto; gap: 4px 10px; align-items: baseline; padding: 8px 0; border-bottom: 1px solid var(--rule); }
  .evrow .mark { font-family: var(--font-mono); text-align: center; color: var(--muted); }
  .evrow[data-level='needs'] .mark { color: var(--needs-text); }
  .evrow[data-level='problem'] .mark { color: var(--bad-text); }
  .evrow[data-level='done'] .mark { color: var(--good); }
  .evrow .when { font-family: var(--font-mono); font-size: 12px; color: var(--muted); font-variant-numeric: tabular-nums; }
  .evrow .what p { margin: 0; }
  .evrow .title { font-size: 15px; line-height: 1.4; color: var(--ink); overflow-wrap: anywhere; }
  .evrow.unread .title { color: var(--ink-strong); font-weight: 500; }
  .evrow.unread .when::after { content: ' •'; color: var(--accent-text); }
  .evrow .detail { font-size: 13px; color: var(--ink-soft); overflow-wrap: anywhere; }
  .evrow .meta { display: flex; flex-wrap: wrap; gap: 2px 12px; font-family: var(--font-mono); font-size: 12px; color: var(--muted); }
  .evrow .meta .open { color: var(--needs-text); }
  .evrow.cleared .title, .evrow.cleared .detail { color: var(--muted); }
  .evrow .acts { display: flex; gap: 12px; font-size: 13px; white-space: nowrap; }
  .evrow .acts .link { border: 0; padding: 0; background: none; color: var(--accent-text); font: inherit; cursor: pointer; text-decoration: underline; }
  @media (max-width: 600px) {
    .evrow { grid-template-columns: 1.5em minmax(0, 1fr); }
    .evrow .when { grid-column: 2; }
    .evrow .what, .evrow .acts { grid-column: 2; }
  }
</style>
