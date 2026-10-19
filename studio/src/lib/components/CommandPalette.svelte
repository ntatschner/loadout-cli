<script lang="ts">
  // Ctrl or Cmd K from anywhere: go to a page, find work in flight, or do
  // something, every action showing the command it runs. A combobox in a
  // modal dialog, controlling a listbox through aria-activedescendant. Given
  // a search, what is typed is also looked for everywhere once it is two
  // characters long and typing has paused, and what that finds follows the
  // palette's own rows.
  import Dialog from './Dialog.svelte';
  import { onDestroy, tick } from 'svelte';
  import { paletteRows, type PaletteGroup, type PaletteRow } from '../palette';

  interface Props {
    groups: PaletteGroup[];
    placeholder?: string;
    onclose?: () => void;
    /** Looks for what is typed everywhere, as more groups. */
    search?: (query: string) => Promise<PaletteGroup[]>;
  }

  let { groups, placeholder = 'Go to a page, find work, or do something', onclose = undefined, search = undefined }: Props = $props();

  const id = `ls-pal-${Math.random().toString(36).slice(2, 7)}`;
  let query = $state('');
  let active = $state(0);
  let list = $state<HTMLDivElement | undefined>();

  let found = $state<PaletteGroup[]>([]);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let asked = 0;

  $effect(() => {
    const typed = query.trim();
    const mine = ++asked;

    clearTimeout(timer);

    if (!search || typed.length < 2) {
      found = [];

      return;
    }

    timer = setTimeout(() => {
      search(typed).then((groups) => mine === asked && (found = groups));
    }, 250);
  });

  onDestroy(() => clearTimeout(timer));

  const shown = $derived([...paletteRows(groups, query), ...found]);
  const flat = $derived(shown.flatMap((group) => group.items));

  $effect(() => {
    // Back to the first row as the query changes, and never past the last.
    query;
    active = 0;
  });

  $effect(() => {
    const index = active;

    tick().then(() => document.getElementById(`${id}-o${index}`)?.scrollIntoView?.({ block: 'nearest' }));
  });

  function run(row: PaletteRow) {
    onclose?.();
    row.run?.(row);
  }

  function keys(event: KeyboardEvent) {
    const count = flat.length;

    if (event.key === 'Enter') {
      event.preventDefault();

      if (flat[active]) {
        run(flat[active]);
      }

      return;
    }

    if (event.key === 'Home' && event.ctrlKey) {
      event.preventDefault();
      active = 0;

      return;
    }

    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && count) {
      event.preventDefault();
      active = (active + (event.key === 'ArrowDown' ? 1 : -1) + count) % count;
    }
  }
</script>

<Dialog label="Command palette" extraClass="pal" initialFocus="input" {onclose}>
  <div class="ls-start">
    <div class="field">
      <input
        name="palette"
        role="combobox"
        aria-expanded="true"
        aria-controls={`${id}-list`}
        aria-autocomplete="list"
        aria-label={placeholder}
        aria-activedescendant={flat.length ? `${id}-o${active}` : undefined}
        {placeholder}
        autocomplete="off"
        bind:value={query}
        onkeydown={keys}
      />
      <span class="ls-kbd" aria-hidden="true">Esc</span>
    </div>
  </div>
  <div bind:this={list} id={`${id}-list`} class="ls-plist" role="listbox" aria-label="Results">
    {#if !flat.length}
      <p class="ls-pempty">Nothing matches “{query}”. Try a page name, a project, or a word from a session.</p>
    {:else}
      {#each shown as group, groupIndex (group.name)}
        <div class="ls-pgroup" role="group" aria-labelledby={`${id}-g${groupIndex}`}>
          <h3 id={`${id}-g${groupIndex}`}>{group.name}</h3>
          {#each group.items as row (row.key)}
            {@const index = flat.indexOf(row)}
            <!-- svelte-ignore a11y_click_events_have_key_events -->
            <div
              role="option"
              id={`${id}-o${index}`}
              class="ls-popt"
              class:need={row.need}
              aria-selected={index === active ? 'true' : 'false'}
              tabindex="-1"
              onclick={() => run(row)}
              onmousemove={() => (active = index)}
            ><span class="g" aria-hidden="true">{row.glyph ?? '→'}</span><span class="t">{row.label}</span><span class="s">{row.sub ?? ''}</span><span class="k">{row.kind ?? ''}</span></div>
          {/each}
        </div>
      {/each}
    {/if}
  </div>
  <div class="ls-pfoot" aria-hidden="true"><span>↑ ↓ to move</span><span>Enter to go or do</span><span>Esc to close</span></div>
</Dialog>
