# MapFilterBar

MapFilterBar is the filter bar over Home's Map and Lens: the presets, then a chip per kind and per state, then the projects to hide. Every change goes straight to config through `onchange`; the bar keeps nothing of its own, so it reads what `loadout config set` set it to as well as what it set. Ported from main's `studio/src/lib/components/MapFilterBar.svelte` and `lib/mapFilter.ts` as built.

## What it shows

- **Presets**, as a segmented switch like Home's Map / Lens: **In flight** (team runs, sessions and tasks that are waiting on you, running or failed: what the map drew before there were filters), **Runs and sessions** (the same, tasks off as well) and **Everything**. When the filters match none of them, **Custom** shows beside them.
- **Kind** chips: Team runs, Sessions, Ideas, Tasks. **State** chips: Waiting on you, Running, Failed, Ended. A chip that is off is struck through as well as unfilled, so the state is not carried by colour alone. The last kind or state on stays on ("One kind stays on", "One state stays on"): a filter showing nothing cannot be reached from the bar.
- **Projects**, folded: "Projects · all shown" or "Projects · starstats, (no project) hidden". Open, a chip per project, pressed while it shows. Projects names what to hide, so a project added later shows without anyone choosing it.
- The commands that write the filters, as a From line that follows the commands setting: `$ loadout config set studio-map-kinds …`, `studio-map-states …` and `studio-map-projects …`. In flight is the three keys unset (`""`), as Home writes it.

## What you provide

- `filter`: `{ kinds, states, projects }`. `MapFilterBar.filterOf(prefs)` reads it from the preferences (`mapKinds`, `mapStates`, `mapProjects`, and the old `mapScope`); a kind or state key that is unset or names nothing known reads as In flight's, so the map is never blank by accident.
- `projects`: every project the bar offers: those with work in view, then any hidden that have none, `(no project)` last. With none, there is no Projects list.
- `onchange(next)`: write the keys that changed (`MapFilterBar.keysOf(next)`; In flight unsets all three) and pass the new filter back with `set({ filter })`.
- Helpers as main's: `KINDS`, `STATES`, `KIND_NAMES`, `STATE_NAMES`, `PRESETS`, `PRESET_NAMES`, `NO_PROJECT`, `presetOf`, `toggled`.

## Do and don't

- Do put it above the Map or Lens, only when Home has any lines, and say under the view what the filters hide, with Show everything (main's "3 hidden, 1 waiting on you. Show everything").
- Do keep the header's count, the network status and the spend counting everything: the filters narrow only what is drawn.
- Don't keep a filter of your own: show what config holds.

## Accessibility

A region labelled "What Home shows", with groups "Presets", "Kind", "State" and "Projects shown". Every preset and chip is a toggle button with `aria-pressed`; the last chip on is `aria-disabled` with its reason as a title. The project list is a native `details`. Focus stays on the button pressed when the bar is redrawn.
