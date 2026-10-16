// The command palette's three groups: Go to a page, find what is In flight,
// and Do something, every action carrying the command it runs.
import { actionsFor } from './actions';
import type { PaletteGroup, PaletteRow } from './palette';
import type { Studio } from './state.svelte';
import { wordFor } from './signals';

const PAGES: [string, string][] = [
  ['home', 'Home'],
  ['sessions', 'Sessions'],
  ['ideas', 'Ideas'],
  ['teams', 'Teams'],
  ['memory', 'Memory'],
  ['context', 'Context'],
  ['spend', 'Spend'],
  ['projects', 'Projects'],
  ['settings', 'Settings'],
];

export function paletteGroups(
  studio: Studio,
  go: (...parts: string[]) => void,
  open: { launch: () => void; ambient: () => void; appearance: () => void },
): PaletteGroup[] {
  const goTo: PaletteRow[] = PAGES.map(([key, name]) => ({ key: `go-${key}`, glyph: '→', label: name, sub: 'page', kind: 'page', run: () => go(key) }));

  const flight: PaletteRow[] = studio.lines.map((line) => ({
    key: `in-${line.id}`,
    glyph: line.waiting ? '!' : '▶',
    label: line.title,
    sub: `${line.where} · ${wordFor(line, studio.idleAfter)}`,
    need: !!line.waiting,
    kind: line.kind,
    run: () => (line.kind === 'session' ? go('session', line.ref) : go('home', line.id)),
  }));

  const doing: PaletteRow[] = [];

  if (!studio.watchOnly) {
    doing.push({ key: 'do-start', glyph: '+', label: 'Start a session…', sub: 'opens New session · $ loadout launch <project>', kind: 'action', run: open.launch });

    // What each piece of work in flight offers, beside its own command.
    for (const line of studio.lines) {
      for (const action of actionsFor(studio, line, go)) {
        if (!action.onpress || action.cmd?.includes('changes nothing')) {
          continue;
        }

        doing.push({
          key: `do-${line.id}-${action.label}`,
          glyph: action.glyph ?? '▶',
          label: `${action.label}: ${line.title}`,
          sub: action.cmd,
          need: !!line.waiting,
          kind: 'action',
          run: () => void action.onpress?.(),
        });
      }
    }
  }

  doing.push(
    { key: 'do-ambient', glyph: '◎', label: 'Enter Ambient', sub: 'changes nothing · A', kind: 'action', run: open.ambient },
    { key: 'do-appearance', glyph: '◐', label: 'Appearance…', sub: 'theme, accent, density, motion, halo', kind: 'action', run: open.appearance },
  );

  return [
    { name: 'Go to', items: goTo },
    { name: 'In flight', items: flight },
    { name: 'Do', items: doing },
  ];
}
