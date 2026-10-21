<script lang="ts">
  // The agent's own screen inside Studio: a slim bar saying what the session
  // is doing, the screen in Studio's terminal palette, and, when the session
  // needs something or has stopped, a bar underneath that says so in words.
  // Built on xterm.js, themed from the term-* tokens. It only draws and
  // takes keys: the page that holds it owns the connection.
  import { onMount } from 'svelte';
  import { Terminal } from '@xterm/xterm';
  import { FitAddon } from '@xterm/addon-fit';
  import { WebglAddon } from '@xterm/addon-webgl';
  import { SearchAddon } from '@xterm/addon-search';
  import '@xterm/xterm/css/xterm.css';
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import { TERMINAL_STATES, largestFitting, resumeCommand, type TerminalState } from '../terminal';
  import { SPLIT_SETTLED } from '../splits';

  /** How long a new size waits for the next before the session is told. */
  const RESIZE_HOLD = 200;

  interface Props {
    title: string;
    project?: string | null;
    agent?: string | null;
    status: TerminalState;
    /** The exit code, for the error state. */
    code?: number | null;
    /** The agent's own id for the conversation, for 'loadout resume'. */
    agentSession?: string | null;
    /** Whether a screen reader is in use, which switches on xterm's own mode for one. */
    screenReader?: boolean;
    /** Where Ctrl+] sends focus. */
    leaveTo?: HTMLElement;
    /** Whether this window may type; another window may hold the input. */
    holds?: boolean;
    oninput?: (data: string) => void;
    onresize?: (columns: number, rows: number) => void;
    onretry?: () => void;
    /** Told how the screen is drawn: webgl, or dom where WebGL could not start. */
    ondrawn?: (renderer: string) => void;
  }

  let {
    title,
    project = null,
    agent = null,
    status,
    code = null,
    agentSession = null,
    screenReader = false,
    leaveTo = undefined,
    holds = true,
    oninput = undefined,
    onresize = undefined,
    onretry = undefined,
    ondrawn = undefined,
  }: Props = $props();

  const shown = $derived(TERMINAL_STATES[status]);
  const ask = $derived(shown.ask);

  let host = $state<HTMLDivElement | undefined>();
  let size = $state(13);
  let fit = $state(typeof window !== 'undefined' && window.innerWidth < 600);
  let columns = $state(80);
  let rows = $state(24);
  // Reactive, so the effects below apply once it exists as well as on every change after.
  let terminal = $state.raw<Terminal | null>(null);
  let fitter: FitAddon | null = null;
  let searcher: SearchAddon | null = null;

  /** Writes what the session printed. */
  export function write(data: Uint8Array | string) {
    terminal?.write(data);
  }

  /** Matches drawn in the needs colour, so a find reads as Studio's own highlight. */
  function findColours() {
    const needs = token('--needs') || '#f0b429';

    return { matchBackground: needs, matchOverviewRuler: needs, activeMatchBackground: needs, activeMatchColorOverviewRuler: needs };
  }

  /** Finds the next match of what is typed, from where the last one was; false where there is none. */
  export function find(term: string, backwards = false): boolean {
    if (!searcher || !term) {
      searcher?.clearDecorations();

      return false;
    }

    const options = { caseSensitive: false, decorations: findColours() };

    return backwards ? searcher.findPrevious(term, options) : searcher.findNext(term, options);
  }

  export function clearFind() {
    searcher?.clearDecorations();
    terminal?.clearSelection();
  }

  /** Puts the keyboard in the terminal, as "Type a reply" and "Go to the question" do. */
  export function focus() {
    terminal?.scrollToBottom();
    terminal?.focus();
  }

  function token(name: string) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  }

  const highContrast = () => (document.documentElement.getAttribute('data-theme') ?? '').startsWith('hc-');

  function theme() {
    const colour = (name: string) => token(`--term-${name}`);

    return {
      background: colour('bg'),
      foreground: colour('fg'),
      cursor: colour('cursor'),
      cursorAccent: colour('bg'),
      selectionBackground: colour('selection'),
      black: colour('black'),
      red: colour('red'),
      green: colour('green'),
      yellow: colour('yellow'),
      blue: colour('blue'),
      magenta: colour('magenta'),
      cyan: colour('cyan'),
      white: colour('white'),
      brightBlack: colour('bright-black'),
      brightRed: colour('bright-red'),
      brightGreen: colour('bright-green'),
      brightYellow: colour('bright-yellow'),
      brightBlue: colour('bright-blue'),
      brightMagenta: colour('bright-magenta'),
      brightCyan: colour('bright-cyan'),
      brightWhite: colour('bright-white'),
    };
  }

  function blinks() {
    const motion = document.documentElement.getAttribute('data-motion') ?? 'full';

    return motion === 'full' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  /** Lays the screen out: 80 columns scaled to the width under Fit, otherwise the text size and as many columns as fit. */
  function layout() {
    if (!terminal || !fitter || !host) {
      return;
    }

    if (fit) {
      const xterm = terminal;
      const measure = fitter;
      // Each size asked about is measured there and then, so the answer is
      // xterm's own cell at that size rather than a guess at it.
      const best = largestFitting((trial) => {
        xterm.options.fontSize = trial;

        return (measure.proposeDimensions()?.cols ?? 0) >= 80;
      });
      terminal.options.fontSize = best;
      const proposed = fitter.proposeDimensions();

      if (proposed && proposed.rows > 0) {
        terminal.resize(80, proposed.rows);
        clearSideways(proposed.rows);
      }
    } else {
      terminal.options.fontSize = size;
      fitter.fit();
    }
  }

  /** At Fit's smallest size 80 columns can still be wider than the box, and the sideways scrollbar then takes a strip off the bottom: the rows give way, so the last one is never drawn under it. */
  function clearSideways(fitted: number) {
    if (!terminal || !host || host.scrollWidth <= host.clientWidth) {
      return;
    }

    const lost = host.offsetHeight - host.clientHeight;
    const drawn = host.querySelector<HTMLElement>('.xterm-screen');
    const row = drawn && terminal.rows > 0 ? drawn.offsetHeight / terminal.rows : 0;

    if (lost > 0 && row > 0) {
      terminal.resize(80, Math.max(1, fitted - Math.ceil(lost / row)));
    }
  }

  function zoom(by: number) {
    size = Math.max(9, Math.min(22, size + by));
    fit = false;
    layout();
  }

  function toggleFit() {
    fit = !fit;
    layout();
  }

  $effect(() => {
    // Typing follows the state and who holds the input.
    if (terminal) {
      terminal.options.disableStdin = !(shown.typing && holds);
    }
  });

  $effect(() => {
    if (terminal) {
      terminal.options.screenReaderMode = screenReader;
    }
  });

  $effect(() => {
    terminal?.textarea?.setAttribute('aria-label', `Terminal: ${title}`);
  });

  function press() {
    if (status === 'reconnecting') {
      onretry?.();
    } else {
      focus();
    }
  }

  onMount(() => {
    if (!host) {
      return;
    }

    const box = host;

    terminal = new Terminal({
      cursorBlink: blinks(),
      fontFamily: token('--font-mono') || 'monospace',
      fontSize: size,
      lineHeight: 1.4,
      // Ligatures stay off: xterm draws one glyph per cell, and the agent's
      // own '->' and '!=' are meant as two characters.
      theme: theme(),
      // An agent's own 24-bit colours are lifted to the same floor the
      // sixteen meet: 4.5:1, and 7:1 in the high-contrast themes.
      minimumContrastRatio: highContrast() ? 7 : 4.5,
      screenReaderMode: screenReader,
      // Find's highlights are decorations, which xterm still calls a proposed API.
      allowProposedApi: true,
    });
    fitter = new FitAddon();
    terminal.loadAddon(fitter);
    searcher = new SearchAddon();
    terminal.loadAddon(searcher);

    // Before open(), so the DOM renderer is never made: it writes <style>
    // elements and style attributes, which Studio's content security policy
    // refuses. Without WebGL the terminal still works and the policy reports
    // what it blocked.
    try {
      const webgl = new WebglAddon();
      webgl.onContextLoss(() => webgl.dispose());
      terminal.loadAddon(webgl);
      ondrawn?.('webgl');
    } catch {
      ondrawn?.('dom');
    }

    // Ctrl+] hands focus back to the page, so a keyboard is never stuck in a
    // screen that takes Tab and the arrows for itself.
    terminal.attachCustomKeyEventHandler((event) => {
      if (event.ctrlKey && !event.altKey && !event.metaKey && (event.key === ']' || event.code === 'BracketRight')) {
        if (event.type === 'keydown') {
          event.preventDefault();

          // Where nothing was named, the page around says where the keyboard
          // goes: the list in Pure Focus, the tab in a deck, Back in a frame.
          if (leaveTo) {
            leaveTo.focus();
          } else {
            box.dispatchEvent(new CustomEvent('leaveterminal', { bubbles: true }));
          }
        }

        return false;
      }

      return true;
    });

    terminal.open(box);
    // Named, as every field on the page is, so a browser can tell it apart.
    terminal.textarea?.setAttribute('name', 'terminal-input');
    terminal.textarea?.setAttribute('aria-label', `Terminal: ${title}`);
    layout();

    const typing = terminal.onData((data) => oninput?.(data));
    // The screen follows a moving divider, or a window being resized, every
    // frame; the session is told the size once it stops for a moment, or the
    // divider is let go, so a full-screen agent redraws once rather than on
    // every pixel.
    const xterm = terminal;
    let held: ReturnType<typeof setTimeout> | null = null;
    const tell = () => {
      if (held) {
        clearTimeout(held);
        held = null;
        onresize?.(xterm.cols, xterm.rows);
      }
    };
    const resizing = terminal.onResize((next) => {
      columns = next.cols;
      rows = next.rows;

      if (held) {
        clearTimeout(held);
      }

      held = setTimeout(tell, RESIZE_HOLD);
    });
    document.addEventListener(SPLIT_SETTLED, tell);

    columns = terminal.cols;
    rows = terminal.rows;
    onresize?.(terminal.cols, terminal.rows);

    // Once a frame at most: under Fit a layout tries several sizes, and a
    // dragged divider reports a new size on every frame it moves.
    let pending = 0;
    const watcher = new ResizeObserver(() => {
      cancelAnimationFrame(pending);
      pending = requestAnimationFrame(() => layout());
    });
    watcher.observe(box);

    const looks = new MutationObserver(() => {
      if (terminal) {
        terminal.options.theme = theme();
        terminal.options.minimumContrastRatio = highContrast() ? 7 : 4.5;
        terminal.options.cursorBlink = blinks();
      }
    });
    looks.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-accent', 'data-motion'] });

    return () => {
      document.removeEventListener(SPLIT_SETTLED, tell);

      if (held) {
        clearTimeout(held);
      }

      looks.disconnect();
      watcher.disconnect();
      cancelAnimationFrame(pending);
      typing.dispose();
      resizing.dispose();
      terminal?.dispose();
      terminal = null;
    };
  });
</script>

<section class="ls-term" class:fit class:off={!shown.typing} aria-label="Live terminal">
  <div class="tbar">
    <Signal word={shown.word} />
    <span>{[project, agent].filter(Boolean).join(' · ')}</span>
    <span class="size">{columns} × {rows}</span>
    <span class="keyhint">Ctrl+] leaves the terminal</span>
    <span class="grow"></span>
    <button type="button" class="tbtn" aria-label="Smaller text" onclick={() => zoom(-1)}>A−</button>
    <button type="button" class="tbtn" aria-label="Larger text" onclick={() => zoom(1)}>A+</button>
    <button type="button" class="tbtn" aria-pressed={fit ? 'true' : 'false'} title="Fit 80 columns to the width" onclick={toggleFit}>Fit</button>
  </div>
  <div class="screen"><div class="mount" bind:this={host}></div></div>
  <div class={`ask ${ask?.family ?? ''}`} role="status" hidden={!ask}>
    {#if ask}
      <p>{ask.text.replace('{code}', String(code ?? 1))}</p>
      {#if ask.button}
        <HaloButton label={ask.button} size="small" kind={ask.family === 'needs' ? 'needs' : ''} cmd={status === 'reconnecting' ? 'reconnects · changes nothing' : 'puts the keyboard in the terminal'} onpress={press} />
      {/if}
      {#if ask.resume}
        <Command why="Carry it on in a terminal:" command={resumeCommand(agentSession, project)} />
      {/if}
    {/if}
  </div>
</section>
