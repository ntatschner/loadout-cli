<script lang="ts">
  // Sessions as tabs: each its state, title, where it runs and its Ctrl+Alt
  // number, with a dot for output since you last looked. The arrow keys move
  // along the tabs; double-clicking one shows it alone.
  //
  // Each tab can be closed, renamed and moved. Closing an ended tab clears it
  // from this browser at once; closing a running one asks whether to hide the
  // tab or end the session, and ending asks again, naming what it ends. The
  // order, the hidden tabs and the cleared ones are this browser's own; only
  // a name goes to the daemon, so every browser shows it. Rename and End are
  // left out of a Studio that is only watching. Everything here is reached
  // by keyboard as well: F2 renames, Delete closes, Ctrl+Shift+arrows move,
  // and the context-menu key or Shift+F10 opens the tab's menu.
  import { tick } from 'svelte';
  import Dialog from './Dialog.svelte';
  import ContextMark from './ContextMark.svelte';
  import Signal from './Signal.svelte';
  import { signalOf } from '../signals';
  import { command } from '../commands';
  import { nudge, place } from '../tabs';
  import { portal } from '../portal';
  import type { DeckSession } from '../modes';

  interface Props {
    sessions: DeckSession[];
    current: string | null;
    controls?: string;
    label?: string;
    /** Running sessions this browser took off the strip. */
    hidden?: DeckSession[];
    /** Whether names can be changed and sessions ended: false where Studio is only watching. */
    changes?: boolean;
    onpick?: (id: string) => void;
    onmaximise?: (id: string) => void;
    onnew?: () => void;
    /** The strip's new order, every tab on it. */
    onmove?: (order: string[]) => void;
    /** A new name, or null for the one it was launched as. */
    onrename?: (id: string, name: string | null) => unknown;
    onhide?: (id: string) => void;
    onunhide?: (id: string) => void;
    onclear?: (id: string) => void;
    onend?: (id: string) => unknown;
  }

  let {
    sessions,
    current,
    controls = undefined,
    label = 'Sessions',
    hidden = [],
    changes = true,
    onpick = undefined,
    onmaximise = undefined,
    onnew = undefined,
    onmove = undefined,
    onrename = undefined,
    onhide = undefined,
    onunhide = undefined,
    onclear = undefined,
    onend = undefined,
  }: Props = $props();

  const hintId = `ls-stabs-${Math.random().toString(36).slice(2, 7)}`;
  const tabs: Record<string, HTMLButtonElement | undefined> = $state({});
  let strip = $state<HTMLElement | undefined>();

  let renaming = $state<string | null>(null);
  let draft = $state('');
  let field = $state<HTMLInputElement | undefined>();

  // One menu at a time: a tab's, or the hidden tabs'.
  let menu = $state<{ for: string | null; x: number; y: number } | null>(null);
  let menuEl = $state<HTMLElement | undefined>();

  let asking = $state<string | null>(null);
  let ending = $state<string | null>(null);

  let drag: { id: string; x: number; moved: boolean } | null = null;
  let dropBefore = $state<string | null | undefined>(undefined);
  let dragging = $state<string | null>(null);
  let swallowClick = false;

  const order = $derived(sessions.map((one) => one.id));
  const byId = (id: string | null) => sessions.find((one) => one.id === id) ?? hidden.find((one) => one.id === id);

  function focusTab(id: string) {
    void tick().then(() => tabs[id]?.focus());
  }

  function keys(event: KeyboardEvent, index: number) {
    const one = sessions[index];

    if (event.ctrlKey && event.shiftKey && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
      event.preventDefault();
      move(one.id, event.key === 'ArrowRight' ? 1 : -1);

      return;
    }

    if (event.key === 'F2' && changes) {
      event.preventDefault();
      startRename(one.id);

      return;
    }

    if (event.key === 'Delete') {
      event.preventDefault();
      close(one.id);

      return;
    }

    if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault();
      openMenu(one.id, tabs[one.id]);

      return;
    }

    const step: Record<string, number> = { ArrowRight: 1, ArrowLeft: -1 };
    let to: number | null = null;

    if (event.key in step) {
      to = (index + step[event.key] + sessions.length) % sessions.length;
    } else if (event.key === 'Home') {
      to = 0;
    } else if (event.key === 'End') {
      to = sessions.length - 1;
    }

    if (to === null) {
      return;
    }

    event.preventDefault();
    tabs[sessions[to].id]?.focus();
    onpick?.(sessions[to].id);
  }

  function move(id: string, by: number) {
    const next = nudge(order, id, by);

    if (next !== order) {
      onmove?.(next);
      focusTab(id);
    }
  }

  /* ---------- closing ---------- */

  function close(id: string) {
    const one = byId(id);

    if (!one) {
      return;
    }

    if (one.ended) {
      // Nothing is running to lose: cleared from this browser at once.
      onclear?.(id);
    } else if (changes) {
      asking = id;
    } else {
      // Only watching: hiding is the one thing to do, and it can be undone.
      onhide?.(id);
    }
  }

  /* ---------- renaming ---------- */

  async function startRename(id: string) {
    menu = null;
    renaming = id;
    draft = byId(id)?.title ?? '';
    await tick();
    field?.focus();
    field?.select();
  }

  function finishRename(save: boolean) {
    const id = renaming;

    if (id === null) {
      return;
    }

    renaming = null;
    const name = draft.trim();
    const one = byId(id);

    if (save && one && name !== one.title) {
      // Emptied, it goes back to the name it was launched as.
      onrename?.(id, name === '' ? null : name);
    }

    focusTab(id);
  }

  function renameKeys(event: KeyboardEvent) {
    event.stopPropagation();

    if (event.key === 'Enter') {
      event.preventDefault();
      finishRename(true);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      finishRename(false);
    }
  }

  /* ---------- the menu ---------- */

  interface Item {
    label: string;
    hint?: string;
    danger?: boolean;
    run: () => void;
  }

  const items = $derived.by((): Item[] => {
    if (!menu) {
      return [];
    }

    if (menu.for === null) {
      return hidden.map((one) => ({ label: `Show ${one.title}`, run: () => { onunhide?.(one.id); onpick?.(one.id); focusTab(one.id); } }));
    }

    const one = byId(menu.for);

    if (!one) {
      return [];
    }

    const list: Item[] = [];

    if (changes) {
      list.push({ label: 'Rename', hint: 'F2', run: () => void startRename(one.id) });

      if (one.launchTitle && one.title !== one.launchTitle) {
        list.push({ label: `Back to “${one.launchTitle}”`, run: () => { onrename?.(one.id, null); focusTab(one.id); } });
      }
    }

    if (order.indexOf(one.id) > 0) {
      list.push({ label: 'Move left', hint: 'Ctrl Shift ←', run: () => move(one.id, -1) });
    }

    if (order.indexOf(one.id) < order.length - 1) {
      list.push({ label: 'Move right', hint: 'Ctrl Shift →', run: () => move(one.id, 1) });
    }

    if (one.ended) {
      list.push({ label: 'Clear', hint: 'Delete', run: () => onclear?.(one.id) });
    } else {
      list.push({ label: 'Hide', run: () => onhide?.(one.id) });

      if (changes) {
        list.push({ label: 'End session…', danger: true, run: () => (ending = one.id) });
      }
    }

    return list;
  });

  async function openMenu(id: string | null, from: HTMLElement | undefined, at?: { x: number; y: number }) {
    const box = from?.getBoundingClientRect();
    menu = { for: id, x: at?.x ?? box?.left ?? 0, y: at?.y ?? box?.bottom ?? 0 };
    await tick();
    menuEl?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
  }

  function closeMenu(back = true) {
    const was = menu;
    menu = null;

    if (back && was) {
      if (was.for) {
        focusTab(was.for);
      } else {
        void tick().then(() => strip?.querySelector<HTMLElement>('.hid')?.focus());
      }
    }
  }

  function choose(item: Item) {
    menu = null;
    item.run();
  }

  function menuKeys(event: KeyboardEvent) {
    const all = Array.from(menuEl?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const at = all.indexOf(document.activeElement as HTMLElement);
    const step: Record<string, number> = { ArrowDown: 1, ArrowUp: -1 };

    if (event.key in step) {
      event.preventDefault();
      all[(at + step[event.key] + all.length) % all.length]?.focus();
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      all[event.key === 'Home' ? 0 : all.length - 1]?.focus();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      closeMenu();
    } else if (event.key === 'Tab') {
      closeMenu(false);
    }
  }

  $effect(() => {
    if (!menu) {
      return;
    }

    const away = (event: PointerEvent) => {
      if (!menuEl?.contains(event.target as Node)) {
        closeMenu(false);
      }
    };

    document.addEventListener('pointerdown', away, true);

    return () => document.removeEventListener('pointerdown', away, true);
  });

  /* ---------- dragging ---------- */

  function down(event: PointerEvent, id: string) {
    if (event.button !== 0 || renaming) {
      return;
    }

    drag = { id, x: event.clientX, moved: false };
  }

  function over(event: PointerEvent) {
    if (!drag) {
      return;
    }

    if (!drag.moved && Math.abs(event.clientX - drag.x) < 6) {
      return;
    }

    if (!drag.moved) {
      drag.moved = true;
      dragging = drag.id;
      (event.currentTarget as HTMLElement | null)?.setPointerCapture?.(event.pointerId);
    }

    // Before the first tab whose middle is past the pointer, or at the end.
    const mine = drag.id;
    const past = order.find((id) => {
      const box = tabs[id]?.getBoundingClientRect();

      return id !== mine && box !== undefined && event.clientX < box.left + box.width / 2;
    });

    dropBefore = past ?? null;
  }

  function up() {
    const was = drag;
    drag = null;

    if (was?.moved) {
      swallowClick = true;
      setTimeout(() => (swallowClick = false), 0);

      if (dropBefore !== undefined) {
        const next = place(order, was.id, dropBefore);

        if (next.join() !== order.join()) {
          onmove?.(next);
        }
      }
    }

    dragging = null;
    dropBefore = undefined;
  }

  function picked(id: string) {
    if (!swallowClick) {
      onpick?.(id);
    }
  }

  const asked = $derived(byId(asking));
  const ended = $derived(byId(ending));
</script>

<div class="ls-stabs" bind:this={strip}>
  <!-- svelte-ignore a11y_interactive_supports_focus -->
  <div role="tablist" aria-label={label} onpointermove={over} onpointerup={up} onpointercancel={up}>
    {#each sessions as one, index (one.id)}
      <div
        class="tab"
        class:dragging={dragging === one.id}
        class:before={dragging !== null && dropBefore === one.id}
        class:after={dragging !== null && dropBefore === null && index === sessions.length - 1}
        data-id={one.id}
      >
        {#if renaming === one.id}
          <input
            bind:this={field}
            bind:value={draft}
            class="rename"
            type="text"
            maxlength="80"
            aria-label={`New name for ${one.title}`}
            aria-describedby={hintId}
            onkeydown={renameKeys}
            onblur={() => finishRename(true)}
          />
          <span class="hint" id={hintId}>
            Enter saves, Escape cancels, empty goes back to “{one.launchTitle ?? one.title}”.
            {#if one.kind === 'launch'}{one.namesAgent ? 'Its agent is given the name too.' : 'Only Loadout shows this name: its agent has not said it can take one.'}{/if}
          </span>
        {:else}
          <button
            bind:this={tabs[one.id]}
            type="button"
            role="tab"
            class:needs={signalOf(one.word).family === 'needs'}
            aria-selected={one.id === current ? 'true' : 'false'}
            tabindex={one.id === current ? 0 : -1}
            aria-controls={controls}
            aria-keyshortcuts={index < 9 ? `Control+Alt+${index + 1}` : undefined}
            aria-haspopup="menu"
            data-id={one.id}
            onclick={() => picked(one.id)}
            ondblclick={() => onmaximise?.(one.id)}
            onkeydown={(event) => keys(event, index)}
            onpointerdown={(event) => down(event, one.id)}
            oncontextmenu={(event) => { event.preventDefault(); void openMenu(one.id, tabs[one.id], { x: event.clientX, y: event.clientY }); }}
          >
            <Signal word={one.word} />
            <span class="t">{one.title}</span>
            <span class="x">{[one.where, one.agent].filter(Boolean).join(' · ')}</span>
            {#if one.context !== undefined && one.context !== null}<ContextMark percent={one.context} />{/if}
            {#if index < 9}<span class="k" aria-hidden="true">⌃⌥{index + 1}</span>{/if}
            {#if one.unread}<span class="new" title="New output since you looked"><span class="ls-vh">new output</span></span>{/if}
          </button>
          <button
            type="button"
            class="close"
            tabindex="-1"
            aria-label={`Close ${one.title}`}
            title={one.ended ? 'Clear this tab' : 'Hide this tab, or end the session'}
            onclick={() => close(one.id)}
          >×</button>
        {/if}
      </div>
    {/each}
  </div>
  {#if hidden.length}
    <button type="button" class="hid" aria-haspopup="menu" aria-expanded={menu?.for === null ? 'true' : 'false'} onclick={(event) => void openMenu(null, event.currentTarget as HTMLElement)}>
      {hidden.length} hidden
    </button>
  {/if}
  {#if onnew}
    <button type="button" class="add" aria-label="New session" title="New session" onclick={onnew}>+</button>
  {/if}
</div>

{#if menu && items.length}
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div
    bind:this={menuEl}
    use:portal
    class="ls-tabmenu"
    role="menu"
    tabindex="-1"
    aria-label={menu.for === null ? 'Hidden tabs' : `Tab ${byId(menu.for)?.title ?? ''}`}
    style={`left:${Math.round(menu.x)}px;top:${Math.round(menu.y)}px`}
    onkeydown={menuKeys}
  >
    {#each items as item (item.label)}
      <button type="button" role="menuitem" tabindex="-1" class:danger={item.danger} onclick={() => choose(item)}>
        <span>{item.label}</span>{#if item.hint}<kbd>{item.hint}</kbd>{/if}
      </button>
    {/each}
  </div>
{/if}

{#if asked}
  <Dialog
    title={`Close ${asked.title}?`}
    why="It is still running. Hide takes the tab off this browser's strip and leaves the session running, still on the Sessions page. End session stops its agent."
    actions={[
      { label: 'Hide', solid: true, cmd: 'this browser only · changes nothing anywhere else', onpress: () => onhide?.(asked.id) },
      { label: 'End session…', kind: 'danger', cmd: 'asks once more', onpress: () => { ending = asked.id; } },
    ]}
    onclose={() => (asking = null)}
  />
{/if}

{#if ended}
  <Dialog
    danger
    title={`End ${ended.title}?`}
    why={`This stops its agent${ended.project ? ` in ${ended.project}` : ''}, and cannot be undone. The session stays listed, ended, so you can still see how it ended.`}
    actions={[{ label: 'End session', kind: 'danger', solid: true, cmd: command.endHosted(ended.id), onpress: () => onend?.(ended.id) }]}
    onclose={() => (ending = null)}
  />
{/if}

