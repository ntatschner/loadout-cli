Loadout Studio is the design system for Studio: the app Loadout serves on this machine, installable as a PWA, where you start sessions, watch them run, and steer ideas, tasks, team runs, memory and spend. It is separate from Loadout Kit, which stays the system for the classic dashboard. Studio's look is an instrument panel lit from inside: dark ground, thin type, rings of light that breathe while work moves. Build every Studio screen from these tokens and classes; the final app is written in Svelte, so components are plain CSS classes and small helpers that port line for line.

## Principles

1. **Work moves, and you can see it move.** Things travel between stages, rings breathe while something works, and the thing that most needs you rises while the rest recede.
2. **Answer where you see it.** Anything waiting for you can be answered where it is shown. You never have to open a detail view to unblock something.
3. **Every action has its command.** Anything that changes something has the exact `loadout` command it runs, in `cmd` style, beside or under the control (`$ loadout team gate <run> --answer yes`). When it shows is yours to choose (`studio-commands`): on hover (the default: while you point at or tab to the control, in a line kept for it, so nothing moves), always, or never. Terminal-only commands, the Commands page and a disabled button's reason show whatever the setting. Two things never get a button, only the command to run in a terminal: reading or setting a secret (`$ loadout team attach set --passphrase "…"`), and agreements that must be made in a terminal (`$ loadout tools verify <draft> --agree`, `$ loadout team remedy trust <remedy>`).
4. **A state is a word, a glyph, a colour and a motion.** All four, every time. Colour repeats the state; it never carries it alone.
5. **Four themes, all measured.** Dark, Light, High contrast dark and High contrast light are designed and checked separately. None is a filter over another.

## Content fundamentals

- Use the command line's words: sessions, ideas, tasks, team runs, rounds, gates. Say "waiting for you" and "needs a decision", not "pending" or "action required".
- Address the reader as *you*. Name work by what it is for ("Retry the upload with backoff"), never by an id alone.
- Join short facts with a middle dot: "line 4 · team run · release-crew".
- State words are lower case and come from Loadout: working, idle, running, done, failed, stopped, limited, blocked, needs a decision. Stage names are mixed case: Captured, Shaping, Running, Needs you, Done.
- Labels, buttons and zone headings are set in spaced capitals by their style (`label-*`); write them in sentence case in the source ("Leave ambient"), never in capitals.
- Say what something costs in tokens. Spend thresholds say where you stand; they never stop anything, so never word them as a limit.
- No emoji, no exclamation marks.

## Visual foundations

### Themes

- Set `data-theme` on `<html>`: `dark` (the default), `light`, `hc-dark`, `hc-light`. With no saved choice, use `LoadoutStudio.preferredTheme()`, which follows the operating system.
- **Dark** is the boards' look: `ground` #03070c, `ink` #d9f3ff, and light from the accent. Its values are the chosen boards' exact values.
- **Light** is a daylight instrument: white-blue paper (`ground` #f3f7fa, `surface` white), crisp dark ink, and the accent as solid strokes. There is no glow; shapes and motion stay the same as Dark.
- **High contrast dark** and **High contrast light** meet WCAG AAA: every text token is 7:1 or better on `ground`, `surface` and `surface-2`; surfaces are set apart by `rule` borders at 3:1, not by shade; no glow; focus is yellow (`hc-dark`) or blue (`hc-light`).

### Colour

- Put text in `ink` on `ground`, `surface` or `surface-2`. Use `ink-strong` for what must read first (titles, numbers), `ink-soft` for zone headings, `muted` for secondary facts, and `subtle` for commands, placeholders and times.
- Every text token is 4.5:1 or better on all three grounds in Dark and Light, and 7:1 or better in both high-contrast themes. The tightest pair is 5.6:1 in Light (`good` on `surface-2`).
- Raise panels with `surface` (dialogs, popovers, toasts, the palette, the widened rail) and `surface-2` (a hovered row, a code well, a meter track). Dim the page behind a dialog with `scrim`.
- Draw grouping hairlines in `rule`, or in the accent mixed at `tint-line` for the line after a zone heading. Draw the edge of an unfilled control in `control-edge` (3:1 or better everywhere).
- **Accent.** Read `accent` for rings, strokes and fills, `accent-text` for accent words, `on-accent` for text on an accent fill. The person picks Cyan (default), Ice, Magenta or Violet with `data-accent`; the named tokens (`accent-cyan`, `accent-cyan-text`, `on-accent-cyan` and so on) exist only to be rebound. The accent never means a state.
- **Signals.** Five families carry every state, each as a word, a glyph, a colour and a motion:

| Family | Token | States | Motion (full) |
|---|---|---|---|
| Moving | `moving` | working, running, doing, round running | breathe; spin for a round |
| Needs | `needs`, `needs-text`, `needs-wash` | needs a decision, waiting for you, needs approval, quiet, projected over, changed since trusted | pulse, then a glow that stays |
| Bad | `bad`, `bad-text`, `bad-wash` | failed, last round failed, blocked, limited, exited with an error, lost, unmet | none: a hard edge |
| Good | `good` | done, met, accepted, ended, trusted | one settle flash, then still |
| Rest | `rest` | idle, open, captured, shaping, held, stopped, not attempted, unknown, dropped, connecting, reconnecting, only watching, no verdict, not trusted, scheduled | dim (Dark only), dashed or struck through; never a loop |

- `good` and `bad` are close in lightness, so they are always told apart by glyph and word (a check against a cross), never by colour alone. `moving` sits near the Cyan accent for the same reason: a Moving thing always shows its play glyph and its word.
- Fill washes (`needs-wash`, `bad-wash`) only ever sit under their text colour and a 3px edge in the family colour.
- `gauge` is the Gyroscope halo's spend ring. It is always labelled "Spend".
- Put text on a solid signal fill in `on-signal`.
- `shaping` colours the Lens halo's Shaping ring only. As a state, shaping belongs to the Rest family.
- The Map gives each piece of work a line colour, `line-1` to `line-8`, for its whole life. Line colours are identity, never state, and its number on a train or badge is `on-line`. They appear only inside the Map parts (TubeMap, LineStrip, NetworkStatus).

### The terminal

- The live terminal has its own sixteen ANSI colours per theme, `term-black` to `term-bright-white`, on `term-bg`, with `term-fg` text, a `term-cursor` that follows the accent, and `term-selection`. Their hues are Studio's: red is `bad`, green `good`, yellow `needs`, blue `moving`, magenta `shaping`. `term-cyan` stays cyan whatever the accent, because agents use it as a colour.
- Every terminal text colour is 4.5:1 or better on `term-bg` and `term-selection` (7:1 in high contrast). Build the terminal on xterm.js with `minimumContrastRatio` 4.5 (7 in high contrast), so an agent's own 24-bit colours are lifted to the same floor.
- Studio never types into a session for you, and never puts an agent's choices on buttons. It is told that a session is waiting (from the agent's own hooks), not what the choices are, so the question is answered in the terminal.

### Teams

- A team run is shown by what it is for, its state, its round and its spend. What it asks is answered where it is shown: on the Teams page under **Needs you**, on its card and on its page, with a Gate. Each kind of question has its own answers: the lead's question (its options, the recommended one solid, and Think again), the lead's readings of the done-when, a brief in a manual run (in a box you can change before it goes), the lead's proposed done-when (in a box), a confirmation or a permission (yes or no), and its budget (raise it, take the cap off, or a figure).
- **Needs you** holds every reason a run wants you, and each clears itself: it asked something, the lead took a round without asking for anything, a node is quiet for three times its own usual turn, or its budget is spent or projected to be. Nothing is dismissed by hand.
- A run's page draws its rounds as the lead's line with the nodes each round briefed hanging from it (RunShape), then Done when, Who is on it, and **five depths** on keys 1 to 5: What it said, What it cost, The papers, What it changed and What it delivered.
- **What it delivered** is the Outbox: the run's files in a tree beside a viewer that previews Markdown, code, CSV, JSON and images, shows each file's change, and downloads one file or all of them as a .zip. It only reads, so it needs no command, but each file shows the terminal line that reads it.
- Saying something to the lead (`team message`) needs only Studio's sign-in. Typing at a live node (`team say`) needs a second credential: the passphrase, swapped for a 30-minute grant held only by the page. Setting or clearing that passphrase, and trusting a remedy, happen in a terminal.
- Projected spend past a budget is said in words with a Needs edge, never in colour alone. Stopping is "after this round": nothing is killed mid-turn.

- **Is it getting closer?** A run carries a ProgressLine ("3 of 5 met, up from 1 · last moved 2 rounds ago") on its card and board row, and its page has a CriteriaGrid: each done-when round by round. A run where no done-when has moved for three rounds is **stalled**: held after its round, and under Needs you with three answers (let it carry on, tell it something, stop it).
- A finished run is picked up with a PickUpSheet that asks for exactly what it ran out of; its page lists what it was refused and decided (RefusalsList).
- **The office.** The Teams page has an **Office** view beside its runs: the classic dashboard's pixel-art building, one floor per run, drawn by the same engine in Studio's frame (OfficeView). The art keeps its own colours in every theme; the frame round it follows the theme. Tab into it and the arrow keys move a two-tone focus ring between floors, people and rooms; Enter opens; Escape goes back. A room or a person opens an OfficePanel with the classic popup's actions as Studio buttons that show their commands, and the lead's office can be named (`loadout team name`). It shows in Default, Team Focus and Terminal Focus (beside the terminals), never in Minimal or Pure Focus.

### Modes

- One switch re-lays out the whole of Studio around what you are doing. It is `data-mode` on `<html>`, kept in this browser, and cycled with Ctrl+Shift+M:
  - **Default**: Studio as it is.
  - **Terminals** (Terminal Focus): sessions as tabs over one, two or four big terminals.
  - **Teams** (Team Focus): every live run as a line on one board, each run or team opening beside it.
  - **Minimal**: one quiet column of what is live; open one and it is the whole screen.
  - **Pure** (Pure Focus): flat and sharp, for working. List, view and actions in three panes over a command bar. Every action is numbered and every key is printed beside what it does: J and K move, Enter opens, 1 to 9 act, / filters, : commands, G then a letter goes, F6 changes pane, ? lists every key. No glow and no loops; the only motion is a 120ms flash on what you ran.
- The ModeBar is at the top of every mode, with whatever is waiting on you, so a focus mode never hides a question.
- Changing mode is one animated step (`morph()`): the page sinks, the new layout rises, and the rail and the mode bar travel on their own. Under reduced motion it fades; under none it just changes.

### Pages

- Ideas, Projects, Context, Settings, Memory and Spend each have their own shape, built around what is on them, not one shared list beside a page:
  - **Ideas** is a shelf and a workbench: where the idea stands (IdeaStages), its questions answered in the page (QuestionSet), its plan as layers of options side by side (PlanLayer), then Accept it. Notes from elsewhere come in as a NoteDump.
  - **Projects** is a wall of tiles (ProjectTile); a picked project opens under it as a dossier: its facts, a TaskBoard of open, doing and blocked, and its health (HealthList).
  - **Context** leads with what a session would start with, as one bar of specialists against the token budget (LoadStack), for a task and a mode you choose.
  - **Settings** is a section index beside one section. Loadout's own settings are a searchable ConfigList, each changed where it is shown.
  - **Memory** is the stores on the left (StoreList), the topic edited in the middle (TopicEditor, which shows a clash beside your edit and never overwrites), and its health and tools on the right.
  - **Spend** is one number (TokenSplit), where it went (SpendBars), and what is running and was launched.
- Every command a page offers is a control made for it, in place (see Commands as controls). Nothing is a drawer of generic forms.
- What is only ever done in a terminal (secrets, agreements, the attach passphrase, a server that runs until Ctrl+C) is a TerminalOnly block: the command, why, and Copy. Never a button that does it.
- **In every mode.** The six pages follow the mode you are in:
  - **Terminals** and **Teams**: the page opens beside your terminals or the runs board (PageBeside), which stay live. Escape closes it.
  - **Minimal**: the page is one quiet column (QuietPage): one sentence, what needs you or has gone wrong, and Show everything for the rest.
  - **Pure**: the page is a WorkDesk, reached with G and a letter: I Ideas, P Projects, C Context, M Memory, U Spend (usage), O Settings (options).
- **Forgetting a project** (ForgetProject) is on this machine only, or from the Loadout workspace for every machine, with optional cleanup that runs first and is shown command by command. The repository is never touched.

### Built in main

- These came from Studio as main built it (0.103.1), ported as built, so the system shows what ships:
  - **Sessions:** StatusStrip on a hosted terminal's foot (model, context, cost, the plan's five-hour window, specialists, and "old:" when its figures are stale), StatusBlock in a session's Details, ContextMark in a session's tab, ConversationList for past conversations to carry on.
  - **Teams:** BlockedRuns (runs that ended short of their done-when, each with a suggestion and Confirm), TriggerRules, QueuePanel (what waits for a team, its window and limits), Handover (what a run left for you to do), Journal, MiniLine on board rows, and Spend, a run's spend line.
  - **Everywhere:** ListToolbar, the same search, filters and sort over every list; TaskDetail, a task in full; JobResult, one command run from Studio; Splitter and SplitCross, the dividers every split drags and keys the same way; Ask Loadout in the palette (AskPane, AskPlanCard); ProgressRing.
- Where main's look failed a rule here, the port keeps main's intent and meets the rule: stale status figures are muted ink, not 60% opacity, which measured 2.7 to 3.8:1.

### Commands as controls

- Every command a page offers is a control made for it, never a form generated from its options. Choose the pattern by what the command does:
  - **ActionForm**: a command that does one thing to things you name. Its common fields are in place, each labelled in plain words with the option it sets beside it (`--project`); the rest sit under More options with a count. Fields the page already knows (the project, the topic) are shown as facts, not inputs. Its exact line updates as you type, under the fields.
  - **ItemList**: a thing that has members (servers, packs, backups, checkpoints, marks, handoffs). Each member is a row with its facts and its own buttons, each button with its exact command; Add opens the add form in place, and the new member joins the list. The list says where it was read from (`$ loadout mcp list --global --json`).
  - **SettingRow**: one setting, shown as it stands, changed where it is shown: a switch, a choice or a value, then Set it and Clear it, each with its command. Unset says what applies instead.
  - **Readout**: a read-only command, shown as a proper view drawn from its `--json` (figures, a table, a document, findings, or a list), with the parameters that change what it shows above it and Read it again. It never shows the raw output.
  - **ActionRow**: commands that take nothing, as a row of buttons, each with its command.
- **The effect decides the buttons.** A command that only reads has one button and says `reads only`. One that changes something has Preview (`adds --dry-run · changes nothing`) and the button that does it (`changes something`). One that does nothing without an option (`--apply`, `--fix --yes`, `--clean --apply`) previews without it and adds it to act: the option never appears as a checkbox beside a button that says it changes nothing. One that cannot be previewed says so under its button. One that asks as it goes, or starts an agent, opens in a terminal Studio hosts and says `starts a session`.
- **Ask before what can't be taken back.** Removing, deleting, restoring over files, rotating a pin: a Dialog says what happens and what is kept (a backup, the repository), with a button that names the act. Danger is the Bad colour and the word, never the colour alone.
- **Results land where you asked.** A success says what happened in a line under the control and in a toast with the command; a preview shows what would change as a table under it; a read shows its view. Failure says why in words.
- **Terminal only stays terminal only.** Reading a secret back (a token shown once, such as `team webhook enable`), setting one, the attach passphrase, and agreements (`tools trust`, `tools verify --agree`) are TerminalOnly blocks, whatever control is beside them. Taking an agreement back may be a button.
- **Showing commands.** `data-commands` on `<html>` (set with `setCommands`, kept as `studio-commands`) is `hover` by default, or `always` or `never`. A line keeps its place while hidden, so nothing moves when it appears; a button's command appears for that button only, a form's line while the form is in use, a "From" line while its block is pointed at. TerminalOnly blocks and anything inside `[data-commands="always"]` (the Commands page) always show theirs; a disabled reason (`.ls-cmd.reason`) is never hidden. A palette option whose sub-line is a command marks it `.is-cmd`.
- **The Commands page is the expert catalogue.** It keeps every command's full form for people who think in commands. No page links to it as its way of doing something: if a page needs a command, the page gets a control for it.

### Red Alert

- Off by default (`studio-red-alert`). When on and something needs you, the page's edge lights (Needs, or Bad for a problem) and one line says what, with Go to it. It clears itself. It breathes under full motion, is steady under reduced, and is a still border under none and in Pure Focus.

### Background

- Studio's ground is a field map (`FieldMap`): an embossed survey map with contours, a heavier index contour every fifth line, dashed grid squares every 200px, and map references (A, B… along the foot; 01, 02… down the left, under the rail). It is drawn from a fixed seed, so it is the same map everywhere.
- Embossed, not drawn: each contour has a light edge up-left, a dark edge down-right and the line in `accent` (4% in Dark, 5.5% in Light; index contours 7% and 9%). Panels, dialogs and the terminal stay solid.
- It is faint on purpose, and measured: on the map's worst pixel every text colour stays 4.5:1 or better, in both themes and all four accents. Measure again after changing any level.
- `data-backdrop` is `plain`, `map` (still, the default) or `map-animated` (a 16s scan line lights the contours it crosses, a white glint in Light; index contours breathe over 9s). It is set in Appearance → Background and stored as `studio-backdrop`.
- The scan only runs under full motion and outside Pure Focus; everywhere else the map is still. Both high-contrast themes always get the plain ground.
- The map carries on under the nav: the side rail is 58% ground with a 1.5px blur (solid again when it widens over the page), and the rail and the mode bar have an embossed edge with grid-reference ticks.

### Light and glow

- Glow is light from the accent or a signal colour: `0 0 var(--glow-md) -2px var(--glow)` round a ring, `var(--glow-sm)` on a dot, `var(--glow-lg)` on hover and focus.
- `--glow` is derived in `bundle.css` from `accent` at `glow-strength`. It is on in Dark and transparent in Light and both high-contrast themes, so write glows once and let the theme switch them off. Never use glow as the only difference between two states.
- Tint a hovered row with `--tint-hover-c` and a picked row with a fade from `--tint-picked-c`, plus a 3px `accent` edge.

### Type

- Saira (variable, 100–900) for everything you read; JetBrains Mono (variable, 100–800) for commands, times and counts. Both are bundled as self-hosted woff2 under `fonts/`, with their OFL licences under `licenses/`. Studio fetches nothing.
- Display is thin on purpose: `display-xl` 40/200, `display` 30/200, `title` 21/300, `ask` 19/400. In Dark the glow gives thin type its weight; in Light and high contrast the ink does.
- Text: `entry` 20/300 for the start-a-session rail, `body` 15/400, `body-sm` 13.5/400, `small` 12.5/400.
- Labels are spaced capitals at weight 500: `label-zone` (.24em), `label-button` and `label-button-sm` (.22em), `label-tab` (.2em), `label-nav` (.24em).
- Mono: `cmd` 11.5 for every command, `mono-meta` 12 for times, `mono-note` 11 for live counts.
- Never set body text below `small` (12.5px). Mono notes may go to 11px because they repeat what the words already say.

### Space and density

- Use the spacing scale `space-1` (4px) to `space-8` (40px), always multiplied by density: `calc(var(--space-4) * var(--density))`.
- `data-density` picks Compact (0.8), Comfortable (1, the default) or Roomy (1.25). `rail-width`, `rail-width-open` and the control heights are not multiplied, and no control is ever below 24px.
- Controls are `control` (44px) tall, small ones `control-sm` (36px), the start rail `control-lg` (60px).
- Pages start `rail-width` (84px) in, beside the side rail. Below 600px the rail hides behind a Pages tab and the page runs to a 16px (`space-4`) gutter. Nothing ever scrolls sideways at 320px.

### Layers

- Everything fixed to the screen, or able to overlap another region of it, stacks on one scale of `layer` tokens, from the bottom up: `z-ground` (-1, the field map behind the page), `z-page` (10, popovers and suggestions that belong to the page), `z-rail` (20), `z-modebar` (30), `z-focus` (40, Focus view), `z-rail-open` (50, the widened rail and the Pages slide-in, over the mode bar), `z-menu` (55), `z-scrim` (60) and `z-dialog` (61), `z-toast` (70, toasts and Red Alert, over dialogs too), `z-skip` (80) and `z-ambient` (90, which covers the screen).
- A bare number is only for stacking inside one component, such as a badge over its card.

### Shape

- Studio is square: panels, rails, toasts, tabs and the start rail use `radius-none`. Rings, glyph discs and orbiters use `radius-round`; meter tracks use `radius-pill`; signal marks use `radius-chip`.
- Edges are drawn as inset box-shadows or 1px lines, never as rounded cards.

### Motion

- Use `ease-out` for nearly everything and `ease-travel` for things that follow a path. Durations run from `dur-instant` (180ms) to `dur-travel` (900ms); a Moving glyph breathes over `dur-breathe`; a toast folds after `dur-toast-fold`.
- Motion is `full`, `reduced` or `none` (`data-motion`), and the operating system's reduced-motion preference always wins: read the level in force with `LoadoutStudio.motion()`. Under reduced, nothing loops and things fade instead of travelling; under none, nothing animates.
- Nothing flashes more than three times a second.

### Focus and states

- The focus ring is a solid 2px `focus` outline, 2–4px off the control, 3:1 or better on every ground. On a halo button it rings the halo, not the label.
- Hover grows a halo ring and lights its centre; press fills it. Disabled controls say why in words beside them rather than greying out.
- What is less urgent recedes to `recede` (0.72) when something needs you, in Dark only, where text stays 4.5:1 or better. In Light and both high-contrast themes nothing fades, because a fade would take text under its contrast floor; the rising item's edge and light carry it alone.

## Iconography

- Signal glyphs are inline SVG on a 20×20 grid, drawn at 12px (16px in the big form) with a 2 stroke and round caps, inside a ring in the family colour: play (working), a rotating arrow (round running), an exclamation (needs), a cross (failed), a barred circle (blocked), a gauge needle (limited), a check (done), a dash (idle), an open circle (open), a diamond (shaping), two bars (held), a square (stopped), a question mark (unknown) and a slash (dropped). They always sit beside their word.
- Page icons in the side rail are 20×20 line icons with a 1.6 stroke, in `ink`, and `accent` for the current page.
- The halo ring (a 1.5px ring in `accent` with a dot at its centre) is Studio's button. It is not an icon and never appears without its label.
- No emoji. No logo yet: Studio's name is set in Saira until its mark is designed.
