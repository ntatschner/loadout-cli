/** Loadout Studio, exposed as window.LoadoutStudio. Plain JavaScript and CSS classes: no framework. */

export type Theme = "dark" | "light" | "hc-dark" | "hc-light";
export type Accent = "cyan" | "ice" | "magenta" | "violet";
export type Density = "compact" | "comfortable" | "roomy";
export type Motion = "full" | "reduced" | "none";

export declare const THEMES: Theme[];
export declare const ACCENTS: Accent[];
export declare const DENSITIES: Density[];
export declare const MOTIONS: Motion[];

/** Sets data-theme on <html>, or on `el`. Unknown values fall back to "dark". */
export declare function setTheme(theme: Theme, el?: Element): void;
/** Sets data-accent, which rebinds `accent`, `accent-text` and `on-accent`. Default "cyan". */
export declare function setAccent(accent: Accent, el?: Element): void;
/** Sets data-density, which multiplies the spacing scale. Default "comfortable". */
export declare function setDensity(density: Density, el?: Element): void;
/** Sets data-motion. The OS reduced-motion preference still wins. */
export declare function setMotion(level: Motion, el?: Element): void;
/** The motion level in force: the page's setting, lowered to "reduced" when the OS asks for less motion. */
export declare function motion(el?: Element): Motion;
/** A first theme for someone who has not picked one, from the OS colour-scheme and contrast preferences. */
export declare function preferredTheme(): Theme;

/* ---------- Stage 2: controls and signals ---------- */

export interface HaloButtonOptions {
  label: string;
  /** "" (accent), "needs" (amber: answers to a question), "danger" (red: acknowledging a problem). */
  kind?: "" | "needs" | "danger";
  size?: "" | "small" | "big";
  /** The filled ring: the strong answer (Approve). */
  solid?: boolean;
  /** One character drawn in the ring: ✓ ✕ ↻ or an option number. */
  glyph?: string;
  /** The command the button runs, shown under it: "$ loadout team gate --answer Approve". */
  cmd?: string;
  /** When set, the button does nothing and this reason is shown under it instead of a command. */
  disabledReason?: string;
  describedBy?: string;
  onPress?: (e: MouseEvent) => void;
}
/** A word beside a ring of light. Returns the button, or a wrapper holding the button and its command. */
export declare function HaloButton(o: HaloButtonOptions): HTMLElement;

/** A command as a block, with an optional line saying why it is run in a terminal. */
export declare function Command(o: { command: string; why?: string }): HTMLElement;

export interface StartRailOptions {
  label?: string; value?: string; placeholder?: string; action?: string; id?: string;
  /** Builds the command shown under the rail from what is typed. */
  command?: (task: string) => string;
  onDeploy?: (task: string) => void;
}
/** The start-a-session rail: accent tab, input, Deploy, and the command it will run. */
export declare function StartRail(o: StartRailOptions): HTMLElement;

/** role="switch", with On or Off written beside it and where the choice is kept underneath. */
export declare function Switch(o: { label: string; sub?: string; checked?: boolean; onChange?: (on: boolean) => void }): HTMLButtonElement;

/** A tablist with a sliding line of light; arrow keys, Home and End move between tabs. */
export declare function Tabs(o: { tabs: { label: string }[]; selected?: number; label?: string; id?: string; onSelect?: (index: number, panel: HTMLElement) => void }): { el: HTMLElement; panel: HTMLElement; select: (i: number) => void };

/** Rings as radios. The command under the group follows the pick. */
export declare function ModePicker(o: { options: { name: string; desc?: string }[]; selected?: number; label?: string; command?: (name: string) => string; onChange?: (name: string, index: number) => void }): HTMLElement;

export type SignalFamily = "moving" | "needs" | "bad" | "good" | "rest";
/** A state word as its family, glyph path and motion. Unknown words read as Rest. */
export declare function signalOf(word: string): { word: string; family: SignalFamily; glyph: string; motion: string };
/** A state as word, glyph, colour and motion. `big` for sheets and headers. */
export declare function Signal(word: string, o?: { big?: boolean }): HTMLElement;
/** The state word for a piece of work. */
export declare function wordFor(item: { kind?: string; stage?: 0 | 1 | 2 | 3 | 4; waiting?: boolean; blocked?: boolean; failed?: boolean; held?: boolean; quiet?: number }, idleAfter?: number): string;
export declare const SIGNAL_STATES: Record<string, [SignalFamily, string, string]>;
export declare const SIGNAL_GLYPHS: Record<string, string>;

/** What just happened to an item, with its age. Stays until the item is looked at. */
export declare function EventMark(o: { family?: SignalFamily; text: string; age?: string }): HTMLElement;
/** One expanding ring at x, y in a positioned container. Skipped under reduced or no motion. */
export declare function ping(container: HTMLElement, x: number, y: number, family?: SignalFamily): void;

/** Waiting items, most urgent first: a problem before a question, an older question before a newer one. */
export declare function rankUrgent<T extends { waiting?: boolean; problem?: boolean; askedAt?: number }>(items: T[]): T[];
/** Raises one row (ls-rise) and recedes the rest (ls-recede). Pass -1 to clear. */
export declare function applyUrgency(rows: HTMLElement[], risingIndex: number, level?: "needs" | "problem"): void;

export interface MeterOptions {
  value: number; max: number; threshold?: number; label?: string; unit?: string; thresholdLabel?: string; overWords?: string; source?: string;
  format?: (v: number) => string; of?: (v: number) => string;
}
/** A beam with a threshold mark; past the mark the fill turns to Needs and the words say so. Thresholds stop nothing. */
export declare function Meter(o: MeterOptions): { el: HTMLElement; set: (v: number) => void };
/** A ring that fills for stages (0 to 1). */
export declare function ProgressRing(o: { value: number }): { el: HTMLElement; set: (f: number) => void };

export interface ToastAction { label: string; cmd: string; kind?: "" | "needs" | "danger"; onPress?: () => string | void }
/** Turns an empty element into the notification stack, top right. */
export declare function Toasts(region: HTMLElement): {
  add: (t: { level: "done" | "info" | "needs" | "problem"; text: string; cmd?: string; actions?: ToastAction[] }) => number;
  settle: (id: number, words?: string) => void;
  stop: () => void;
};
/** Arrow keys, Home and End move between items and pick as they go. */
export declare function roving(items: HTMLElement[], pick: (index: number) => void): void;

/* ---------- Stage 3: navigation, overlays, the halo and the map ---------- */

export interface RailPage { key: "home" | "sessions" | "ideas" | "teams" | "memory" | "context" | "spend" | "projects" | "settings"; name: string; fact?: string; need?: boolean; href?: string }
/** The side rail: page icons with live counts; widens over the page with a dim on hover or focus. Below 600px (or with narrow) a Pages tab slides it in. */
export declare function SideRail(o: { pages: RailPage[]; current: string; label?: string; narrow?: boolean; contained?: boolean; onSearch?: () => void }): HTMLElement;
export declare const NAV_ICONS: Record<string, string>;

export interface DialogOptions {
  title?: string; label?: string; why?: string; content?: Node; danger?: boolean;
  /** HaloButtons; each closes the dialog unless its onPress returns true. A Cancel is added after them. */
  actions?: HaloButtonOptions[];
  cancelLabel?: string; role?: "dialog" | "alertdialog";
  /** The page behind, which steps back while the dialog is open (blurred and dimmed in Dark only). */
  behind?: HTMLElement; host?: HTMLElement; contained?: boolean; initialFocus?: string; onClose?: () => void;
}
/** A projected panel over a dim. Holds focus, closes on Escape or the dim, returns focus to what opened it. */
export declare function Dialog(o: DialogOptions): { open: () => HTMLElement; close: () => void; readonly panel: HTMLElement | null };

/** A trigger that opens a listbox on a light tether. */
export declare function Picker(o: { label: string; options: { name: string; sub?: string; glyph?: string }[]; selected?: number; onPick?: (option: { name: string; sub?: string }, index: number) => void }): HTMLElement;

export interface PaletteRow { label: string; sub?: string; kind?: string; glyph?: string; need?: boolean; run?: (row: PaletteRow) => void }
/** Ctrl or Cmd K: go to a page, find work in flight, or do something. Rows whose label matches rank above rows that match elsewhere. */
export declare function CommandPalette(o: { groups: { name: string; items: PaletteRow[] }[]; placeholder?: string; bindKeys?: boolean; host?: HTMLElement; contained?: boolean; behind?: HTMLElement }): { open: () => void; close: () => void; unbind: () => void };

export interface HaloOptions {
  /** "orbit" (default) or "gyroscope". Chosen per component in Settings → Appearance and kept in Loadout's config (proposed key studio-halo). */
  style?: "orbit" | "gyroscope";
  /** 0 Captured, 1 Shaping, 2 Running, 3 Needs you, 4 Done. */
  stage: 0 | 1 | 2 | 3 | 4;
  /** What sits in the middle: the focus item's words, Signal and answers. */
  core?: Node;
  /** Gyroscope: how far through the hour the work is (0 to 1). */
  hour?: number;
  /** Gyroscope: spend against its cap (0 to 1); null when there is no cap. */
  spend?: number | null;
  names?: [string, string, string, string, string];
  /** Items circling the halo (Ambient). */
  orbiters?: { title: string; word: string; onPick?: (it: unknown) => void }[];
  orbitRadius?: number;
  tilt?: boolean;
}
/** The Lens halo. */
export declare function Halo(o: HaloOptions): { el: HTMLElement; set: (patch: Partial<HaloOptions>) => void };
export declare const STAGE_NAMES: string[];

export interface TubeLine {
  id: string; code: string; title: string; kind?: string; where?: string; stage: 0 | 1 | 2 | 3 | 4;
  waiting?: boolean; blocked?: string; failed?: string; held?: boolean; quiet?: number; facts?: string; note?: string;
  /** Checks drawn as stops after the interchange: [name, "met" | "unmet" | "pending"]. */
  checks?: [string, string][];
  /** A team run's workers, drawn as branches: [name, state]. */
  workers?: [string, string][];
  /** The last events, newest first, drawn as small ticks before their station (five at most). */
  events?: { stage: number; kind: "asked" | "answered" | "round" | "failed" | "started" | "ended" | "blocked" | "report"; t?: string; text?: string }[];
}
/** The Map: lines in identity colours, stations as stages, Needs you as the interchange. 1280px wide; scrolls inside its own box. */
export declare function TubeMap(o: { lines: TubeLine[]; picked?: string; onPick?: (id: string) => void; stations?: string[]; label?: string }): { el: HTMLElement; set: (patch: { lines?: TubeLine[]; picked?: string }) => void };
/** The picked line drawn straight, with its facts, checks, last events and actions. */
export declare function LineStrip(o: { line: TubeLine; facts?: { k: string; v: string }[]; checks?: [string, string][]; events?: TubeLine["events"]; actions?: HaloButtonOptions[]; stations?: string[] }): HTMLElement;
/** The Map's status band: counts per station, the oldest question, tokens today, disruptions. */
export declare function NetworkStatus(o: { lines: TubeLine[]; clock?: string; oldest?: string; tokens?: string; label?: string }): HTMLElement;

/* ---------- Addendum: the terminal and the session page ---------- */

export type TerminalState = "connecting" | "working" | "idle" | "unknown" | "waiting" | "approval" | "ended" | "error" | "reconnecting" | "watch" | "lost";
/** One line of output as runs of [text, classes]: fg-<ansi>, fg-bright-<ansi>, bg-black, bold, ul. */
export type TerminalRun = [string, string?];
export interface TerminalOptions {
  title?: string; project?: string; agent?: string; label?: string; sessionId?: string;
  state?: TerminalState; size?: [number, number]; fit?: boolean; prompt?: boolean;
  lines?: (TerminalRun[] | string)[];
  /** Where Ctrl+] sends focus; by default the next control on the page. */
  leaveTo?: HTMLElement;
  onInput?: (text: string) => void; onRetry?: () => void; info?: { code?: number };
}
/** The live terminal's reference look: bar, screen and the bar that says what the session needs. Build the real one on xterm.js themed from the term-* tokens. */
export declare function Terminal(o: TerminalOptions): { el: HTMLElement; write: (runs: TerminalRun[] | string) => void; setState: (s: TerminalState, info?: { code?: number }) => void; focus: () => void; readonly state: TerminalState };
export declare const TERMINAL_STATES: TerminalState[];

/** The session page: header, the terminal filling the page, and a side panel that folds away. */
export declare function SessionView(o: {
  line: { title: string; where?: string; agent?: string; mode?: string; time?: string };
  terminal?: TerminalOptions;
  facts?: [string, string][]; loaded?: [string, string][]; events?: [string, string][];
  actions?: HaloButtonOptions[]; details?: boolean;
}): { el: HTMLElement; terminal: ReturnType<typeof Terminal> };

/* ---------- Teams ---------- */

export type GateKind = "question" | "confirm" | "permission" | "outward" | "merge" | "brief" | "readings" | "proposal" | "budget";
export interface GateOptions {
  kind?: GateKind; run?: string; gate?: string; node?: string; asked?: string;
  question: string;
  /** question and budget: the answers, exactly as the run wrote them. */
  options?: string[];
  /** The lead's recommendation (question, readings) or Loadout's first offer (budget). Permissions, merges and outward actions carry none. */
  recommendation?: string;
  /** "12 minutes left, then it goes back to the lead to think again". */
  timer?: string;
  /** readings: [criterion, how the lead reads it]. */
  readings?: [string, string][];
  /** brief: the lead's brief, editable before it goes out (manual runs only). */
  brief?: string;
  /** proposal: the lead's proposed criteria, one per line; held: the project's own, shown and held. */
  proposal?: string[]; held?: string[];
  compact?: boolean; disabledReason?: string;
  onAnswer?: (answer: string, extra: { instead?: string | string[] | null }) => void;
}
/** A question a run stopped to ask, answered where it is shown. Each kind has its own answers and its own command. */
export declare function Gate(o: GateOptions): HTMLElement;

export interface RunSummary { id: string; goal: string; team: string; project?: string; posture?: string; word: string; round?: number; rounds?: number; elapsed?: string; spent?: number; cap?: number | null; uncapped?: boolean; projected?: number; rate?: string }
/** The spend line: what it has spent, of what, and where it is projected to end. */
export declare function Spend(r: RunSummary): HTMLElement;
/** One run in a list, with what it asks answered in place. */
export declare function RunCard(o: { run: RunSummary; href?: string; gate?: GateOptions; reasons?: { text: string; clears: string }[]; actions?: HaloButtonOptions[];
  /** Whether it is getting closer: a ProgressLine under its facts. */
  progress?: Parameters<typeof ProgressLine>[0] }): HTMLElement;

export type NeedReason = "asked" | "silent" | "quiet" | "budget" | "spent" | "stalled";
/** Every reason a run wants you. Each clears itself; nothing is dismissed by hand. */
export declare function NeedsYou(o: { items: { reason: NeedReason; run: string; goal: string; team?: string; href?: string; node?: string; quiet?: string; detail?: string; text?: string; clears?: string; gate?: GateOptions; actions?: HaloButtonOptions[] }[] }): HTMLElement;

/** Rounds along the lead's line, each round's nodes hanging from it. Turns vertical below 620px of its own width. */
export declare function RunShape(o: { rounds: { word: string; lead?: string; nodes?: [string, string][] }[]; next?: string; end?: { word: string; text?: string }; picked?: string; onPick?: (node: string) => void; label?: string }): HTMLElement;

/** Who is on the run: state, what each is doing, turns, cost, refusals, and quiet against its own usual turn. */
export declare function Crew(o: { nodes: { node: string; role: string; model?: string; word: string; doing?: string; quiet?: string; turns: number; cost: number; refused?: number }[]; picked?: string; onPick?: (node: string) => void; label?: string }): HTMLElement;

/** Each criterion, how the lead read it, its verdict and the evidence; delivered refs are checked against what workers reported. */
export declare function DoneWhen(o: { criteria: { text: string; verdict?: "met" | "unmet" | "not-attempted" | null; project?: boolean; meaning?: string; evidence?: string[]; delivered?: string[]; cited?: string[] }[]; by?: string; goal?: string }): HTMLElement;

/** The numbers that change a decision: the rate and projection, where the time went, what went wrong. */
export declare function RunNumbers(o: { rate: string; spent: number; cap?: number | null; projected?: number; by?: string; byRound?: [string, number][]; byNode?: [string, number][]; wrong?: [string, number][] }): HTMLElement;

/** One journal line: [time, text, family?, kind?, sub-agent?, repeats?]. */
export type JournalLine = [string, string, string?, string?, boolean?, number?];
/** The five depths of a run, on keys 1 to 5. */
export declare function RunDepths(o: {
  run: string; selected?: number; node?: string; keys?: boolean; onSelect?: (i: number) => void;
  said?: { journal: JournalLine[]; nodes?: string[]; streams?: Record<string, JournalLine[]>; live?: Record<string, boolean>; say?: Omit<SayToNodeOptions, "run" | "node"> };
  cost?: { numbers?: Parameters<typeof RunNumbers>[0]; exchanges?: [number, string, string, number, number, number, string][] };
  papers?: { summary?: string; pairs: { node: string; role?: string; brief: string; report?: string; instead?: string }[] };
  changed?: { patches: Omit<PatchOptions, "run">[] };
  /** The fifth depth: what it delivered (see Outbox). */
  delivered?: Omit<Parameters<typeof Outbox>[0], "run">;
}): { el: HTMLElement; select: (i: number) => void; showNode: (node: string) => void; unbind: () => void };
export declare function Journal(lines: JournalLine[]): HTMLElement;
export declare function Exchanges(rows: [number, string, string, number, number, number, string][]): HTMLElement;
export declare function Papers(p: { summary?: string; pairs: { node: string; role?: string; brief: string; report?: string; instead?: string }[] }): HTMLElement;

export interface PatchOptions { run: string; node: string; branch?: string; base?: string; goal?: string; files?: [string, number, number][]; diff?: [" " | "+" | "-" | "@", string][]; unmeasured?: boolean; pr?: boolean; disabledReason?: string; onPr?: (o: { draft: boolean; title: string }) => void }
/** A node's patch, file summary first, with Open a pull request. */
export declare function Patch(o: PatchOptions): HTMLElement;

export interface SayToNodeOptions { run: string; node?: string; lead?: boolean; disabledReason?: string; state?: "unset" | "locked" | "open" | "ended"; minutesLeft?: number; onUnlock?: (passphrase: string) => boolean; onSend?: (message: string) => void }
/** Typing at a live node, behind the second credential; or, with lead: true, a message to the lead for its next round. */
export declare function SayToNode(o: SayToNodeOptions): HTMLElement;

/** A team you can run, or a template to copy. */
export declare function TeamCard(o: { name: string; description: string; nodes?: number; lead?: string; posture?: string; budget?: string; doneWhen?: number; origin?: string; template?: boolean; problems?: string[]; href?: string; onRun?: () => void; onCopy?: () => void }): HTMLElement;
/** A team's nodes: the lead, and who each briefs. */
export declare function TeamNodes(o: { nodes: { name: string; role: string; mode?: string; deliverable?: string; delegates?: string[]; instances?: number; worktree?: boolean }[] }): HTMLElement;
/** Rows of [term, value, command?]. */
export declare function TeamRules(o: { rows: [string, string, string?][] }): HTMLElement;
/** How the roles have been doing: per role and model, dearest first, read when opened. Rows: [role, model, runs, total, average, taken %, refused a run]. */
export declare function RoleRecord(o: { rows?: [string, string | null, number, number, number, number, number][]; read?: () => any[]; time?: [string, number, number, number, number][]; open?: boolean; refusedHigh?: number; note?: string; title?: string; delay?: number }): HTMLElement;

export interface SheetTeam { name: string; description: string; nodes: number; lead: string; posture: string; budget?: string; doneWhen?: string[]; template?: boolean; problems?: string[] }
/** Start a run, Make a team, or Run one on a schedule. el is the form; open() shows it as a sheet. */
export declare function TeamSheet(o: { kind: "run" | "make" | "schedule"; teams: SheetTeam[]; projects: { name: string; path: string }[];
  /** The recorded tasks a run can be for (Start a run only): picking one adds --task <id> and fills the goal from its title and note. */
  tasks?: { id: string; title: string; state: string; kind?: string; note?: string | null; project?: string; declared?: string }[]; projectDoneWhen?: Record<string, string[]>; preset?: Record<string, string>; daemon?: boolean; watchOnly?: boolean; onSubmit?: (command: string, values: Record<string, unknown>) => void }): { el: HTMLElement; command: () => string; open: () => ReturnType<typeof Dialog> };
/** Runs that start without you, and whether the daemon will fire them. */
export declare function Schedules(o: { daemon: { running: boolean; paused?: boolean }; items: { name: string; team: string; goal: string; when: string; next?: string; autonomy?: string; usd?: string }[]; onRemove?: (name: string) => void; disabledReason?: string }): HTMLElement;
/** A remedy: what it is, this machine's rule for its kind, whether this script is trusted, and the requests waiting on you. */
export declare function Remedy(o: { team: string; name: string; kind: string; what: string; assumes: string; proves: string; revision: number; by?: string; rule: "trusted" | "ask" | "never"; trust: "trusted" | "not trusted" | "changed since trusted"; requests?: { id: string; node: string; run: string; problem: string; why: string; asked?: string }[]; onRead?: () => void; onApprove?: (id: string, reason: string) => void; onRefuse?: (id: string, reason: string) => void; disabledReason?: string }): HTMLElement;
/** The bin: put things back, empty it for good, and clear out runs. */
export declare function Bin(o: { keptDays?: number; entries: { kind: "run" | "team"; name: string; what?: string; removed: string; left: number; size?: string }[]; onRestore?: (e: unknown) => void; onEmpty?: () => void; onPrune?: (command: string) => void; disabledReason?: string }): HTMLElement;
/** A value as a shell needs it. */
export declare function shellQuote(value: string): string;

/* ---------- Teams: what a run delivered ---------- */

/** One file a run's commits touched, as `loadout team outbox --json` lists it. */
export interface OutboxFile { path: string; change: "added" | "changed" | "removed"; commit: string; node: string; bytes?: number }
/** One file a node reported that no commit carries. bytes is null when nothing is there now. */
export interface OutboxAsset { path: string; kind: string; bytes: number | null; node: string; note?: string }
/** What the viewer is given for a file: text (redacted), an image URL, or a diff for "The change". */
export interface OutboxRead { text?: string; url?: string; width?: number; height?: number; binary?: boolean; diff?: [" " | "+" | "-" | "@", string][]; redacted?: number; truncated?: number; unchecked?: boolean }
/** What a run delivered: a file tree by node and commit (or by folder) beside a viewer that previews Markdown, code, CSV, JSON and images, shows the change, and downloads. Reads only. */
export declare function Outbox(o: {
  run: string; repository: string | null; files: OutboxFile[]; loose?: OutboxAsset[]; missing?: string[];
  by?: "node" | "folder"; picked?: string;
  read?: (f: OutboxFile & OutboxAsset & { loose?: boolean }, mode: "file" | "change") => OutboxRead | Promise<OutboxRead>;
  onPick?: (f: unknown) => void; onDownload?: (f: unknown) => void; onDownloadAll?: () => void; downloadAllReason?: string;
}): HTMLElement;
/** Markdown as nodes in Studio's type. Never HTML from the file; only http and https links are live. */
export declare function Markdown(src: string): HTMLElement;
/** Code with line numbers, in the terminal palette. */
export declare function CodeView(text: string, lang?: string, limit?: number): HTMLElement;
/** A CSV or TSV as a table, numbers right-aligned. */
export declare function CsvTable(text: string, sep?: string): HTMLElement;
export declare function fileKind(path: string): "markdown" | "csv" | "json" | "image" | "text" | "code" | "binary";

/* ---------- Modes ---------- */

export type StudioMode = "default" | "terminals" | "teams" | "minimal";
export declare const MODES: { key: StudioMode; name: string; desc: string; icon: string }[];
/** Run a change of layout as one animated step: a view transition where there is one, a rise-in otherwise, a fade under reduced motion, nothing under none. */
export declare function morph(apply: () => void, o?: { noVT?: boolean; target?: HTMLElement }): ViewTransition | null;
/** Four modes as radios, with a light that travels to the one picked. Arrow keys move; Ctrl+Shift+M (bound by the app) cycles. */
export declare function ModeSwitcher(o: { mode: StudioMode; onChange?: (m: StudioMode) => void; compact?: boolean; vertical?: boolean; label?: string }): { el: HTMLElement; set: (m: StudioMode) => void; readonly mode: StudioMode };
/** The bar at the top of every mode: the switcher, search, and whatever is waiting on you. */
export declare function ModeBar(o: { mode: StudioMode; onMode?: (m: StudioMode) => void; compact?: boolean; onSearch?: () => void; waiting?: { label: string; [k: string]: unknown }[]; onWaiting?: (w: unknown) => void }): { el: HTMLElement; switcher: ReturnType<typeof ModeSwitcher> };

export interface DeckSession { id: string; title: string; word: string; where?: string; agent?: string; unread?: boolean }
/** Every session as a tab with its state, Ctrl+Alt+n on the first nine, and a new-output dot. */
export declare function SessionTabs(o: { sessions: DeckSession[]; current?: string; controls?: string; onPick?: (id: string) => void; onMaximise?: (id: string) => void; onNew?: () => void; label?: string }): HTMLElement;
/** Terminal Focus: tabs over one, two or four panes of Terminal, a find bar, and the keys. The deck makes no terminals itself: terminal(id) does. */
export declare function TermDeck(o: { sessions: DeckSession[]; current?: string; layout?: 1 | 2 | 4; terminal: (id: string) => ReturnType<typeof Terminal>; onPick?: (id: string) => void; onLayout?: (n: number) => void; onNew?: () => void; onShown?: (ids: string[], terms: Record<string, ReturnType<typeof Terminal>>) => void }): { el: HTMLElement; pick: (id: string, focus?: boolean) => void; setLayout: (n: 1 | 2 | 4) => void; terminal: (id: string) => ReturnType<typeof Terminal> | undefined; update: (sessions: DeckSession[]) => void; readonly current: string; readonly layout: number };

export interface BoardRun { id: string; goal: string; team: string; project?: string; word: string; round: number; rounds: { word: string; nodes?: [string, string][] }[]; spent?: number; cap?: number | null; projected?: number; gate?: GateOptions;
  /** Whether it is getting closer: a compact ProgressLine under the row. */
  progress?: { total: number; byRound?: number[]; lastMoved?: number | null; stalled?: boolean } }
/** A run's rounds as a small line, for a board row. Decoration: the row's words say the same. */
export declare function MiniLine(rounds: BoardRun["rounds"], ended: boolean, running: boolean): HTMLElement;
/** Team Focus: every run as a line, the one that needs you first, teams along the top. */
export declare function RunBoard(o: { runs: BoardRun[]; teams?: { name: string; word?: string; fact?: string }[]; team?: string; picked?: string; onPick?: (id: string) => void; onTeam?: (name: string) => void; label?: string }): HTMLElement;
/** Minimal: one column of what is live, what needs you first with its question. */
export declare function QuietList(o: { items: { id: string; kind?: string; title: string; word: string; where?: string; ask?: string }[]; onOpen?: (item: unknown, from: DOMRect) => void; label?: string }): HTMLElement;
/** One thing, the whole screen, grown from the line it was opened from. Escape or Back gives the page back. */
export declare function FocusFrame(o: { title: string; sub?: string; word?: string; content: Node; from?: DOMRect; closeLabel?: string; initialFocus?: string; host?: HTMLElement; onClose?: () => void }): { el: HTMLElement; close: () => void };

/* ---------- Pure Focus ---------- */

export interface DeskItem { id: string; kind?: string; title: string; meta?: string; /** Its state word; leave it empty for an item with no state, such as a section of Settings. */ word: string }
export interface DeskAction { label: string; cmd?: string; kind?: "" | "needs" | "danger"; disabledReason?: string; run?: () => string | void }
export interface BarRow { kind: "action" | "go" | "command" | string; label: string; cmd?: string; run?: () => void }
/** The picked thing's actions, numbered 1 to 9 with their commands. A danger action asks for its key twice. */
export declare function ActionPane(o: { actions: DeskAction[]; emptyText?: string; legend?: boolean }): { el: HTMLElement; run: (index: number) => void; disarm: () => void; readonly armed: boolean };
/** A combobox for actions, places and loadout commands; each match shows what it runs. */
export declare function CommandBar(o: { source: (query: string) => BarRow[]; onDone?: () => void; placeholder?: string }): { el: HTMLElement; input: HTMLInputElement; focus: () => void; readonly open: boolean };
/** Every key in Pure Focus. A Dialog, or the body alone with inline: true. */
export declare function KeyHelp(o?: { groups?: [string, [string[], string][]][]; inline?: boolean }): ReturnType<typeof Dialog> | HTMLElement;
export declare const PURE_KEYS: [string, [string[], string][]][];
/** Pure Focus: list, view and actions in three panes over a command bar. Keys: J K move, Enter opens, Esc back, 1-9 act, / filter, : command bar, G then a letter goes, F6 next pane, ? every key. */
export declare function WorkDesk(o: {
  groups: { key: string; name: string; items: DeskItem[] }[];
  selected?: string; label?: string;
  view: (item: DeskItem) => { el: HTMLElement } | null;
  actions: (item: DeskItem) => DeskAction[];
  commands?: (query: string, picked: DeskItem | null) => BarRow[];
  goKeys?: Record<string, string>; onGo?: (where: string) => void;
  /** false when Studio's single-letter shortcuts are off. */
  keys?: boolean;
  onSelect?: (id: string) => void; onShown?: (item: DeskItem, view: unknown) => void;
}): { el: HTMLElement; select: (id: string, quiet?: boolean) => void; focusList: () => void; openView: () => void; closeView: () => void; refresh: (groups: unknown[]) => void; repaintView: () => void; repaintActions: () => void; readonly selected: string | null; destroy: () => void };

/* ---------- The pages (stage 8) ---------- */

export type Effect = "reads" | "changes" | "session" | "terminal";
export interface DrawerField {
  /** "--project" for an option, a bare name ("id", "topic") for an argument. Arguments are written in field order, before options. */
  name: string; label?: string; value?: string | boolean;
  kind?: "text" | "flag" | "choice"; options?: string[]; lines?: number; placeholder?: string; hint?: string;
  /** Shown, not changeable: the project the page is about. */
  fixed?: boolean; required?: boolean; wide?: boolean;
  /** Write the option as --name=value, for prose that may start with a dash. */
  joined?: boolean;
}
/** One loadout command as a closed drawer: title, `loadout <path>` and its effect. Opened, it is the command's form, the exact line, a Preview (--dry-run) when it has one, and its button. */
export declare function ActionDrawer(o: {
  path: string; title?: string; why?: string; note?: string; effect?: Effect;
  /** Changes only when one of these is given (backup restore: ["--apply"]); otherwise it reads. */
  changesOnlyWith?: string[]; dryRun?: boolean; fields?: DrawerField[]; label?: string; danger?: boolean; open?: boolean;
  /** For effect "terminal": why Studio does not run it, completing "It …". */
  reason?: string; command?: string;
  disabledReason?: string;
  onRun?: (values: Record<string, string | boolean>, info: { preview: boolean; line: string; reads: boolean }) => string | void;
  onOpen?: () => void;
}): { el: HTMLElement; open: () => void; close: () => void; values: Record<string, string | boolean>; line: () => string };
/** Drawers in named groups. */
export declare function ActionDrawers(groups: { name?: string; items: (Parameters<typeof ActionDrawer>[0] | ReturnType<typeof ActionDrawer>)[] }[]): HTMLElement;
/** What is only ever done in a terminal: the command, why, and Copy. Never a button that does it. */
export declare function TerminalOnly(o: { title?: string; why?: string; command: string; fact?: string }): HTMLElement;
export interface Finding { name?: string; severity?: "error" | "warning" | "info" | "ok" | string; detail?: string; fixable?: boolean }
/** Findings, worst first, each with its severity as a word and a glyph. */
export declare function HealthList(o: { findings: Finding[]; verdict?: string; from?: string; noneText?: string }): HTMLElement;
/** Captured, Answering, Ready, Proposed, Accepted: where an idea stands, with its state word under the stage it is at. */
export declare function IdeaStages(o: { stage: string; word?: string }): HTMLElement;
export interface IdeaQuestion { id: string; question: string; why?: string; options?: string[]; recommendation?: string | null; answer?: string | null }
/** The questions an agent asked, answered in the page: an option or your own words. Sent together, with or without another round. */
export declare function QuestionSet(o: { id: string; where: string[]; questions: IdeaQuestion[]; disabledReason?: string; onSend?: (answers: { question: string; answer: string; line: string }[], refine: boolean, lines: string[]) => void }): { el: HTMLElement; readonly changed: IdeaQuestion[] };
export interface IdeaOption { id: string; title: string; detail?: string; pros?: string[]; cons?: string[]; recommended?: boolean }
/** One layer of the plan, its options side by side; or an addition. Choose, keep, drop, or ask for it to be reworked. */
export declare function PlanLayer(o: {
  idea: string; where: string[]; accepted?: boolean; disabledReason?: string;
  layer?: { id: string; name: string; purpose?: string; chosen?: string | null; verdict?: string; options: IdeaOption[] };
  addition?: { id: string; title: string; why?: string; verdict?: string };
  onChoose?: (option: string) => void; onVerdict?: (verdict: "Keep" | "Drop") => void; onImprove?: (request: string) => void;
}): HTMLElement;
/** Notes from elsewhere, split into ideas and tasks; tick the pieces to record. */
export declare function NoteDump(o: { dump: { id: string; source?: string; captured?: string; split?: string | null; items: { number: number; title: string; excerpt?: string; kind: string; recorded?: boolean }[] }; disabledReason?: string; onApply?: (numbers: string[]) => void; onSplit?: () => void; onShow?: () => void }): HTMLElement;
/** One project on the Projects wall; or, with place, a way to add one. */
export declare function ProjectTile(o: { project?: { id: string; name: string; available?: boolean; path?: string; defaultAgent?: string; pinned?: boolean; branch?: string | null; clean?: boolean | null }; href?: string; picked?: boolean; doing?: number; blocked?: number; findings?: number; ideas?: number; place?: string; sub?: string }): HTMLElement;
export interface Task { id: string; title: string; state: "open" | "doing" | "blocked" | "done" | "dropped"; kind?: string; declaredBy?: string; declared?: string; note?: string | null }
/** Open, Doing and Blocked side by side; done and dropped folded away. Each task moves on with `loadout task declare`. */
export declare function TaskBoard(o: { tasks: Task[]; where: string[]; from?: string; disabledReason?: string; onDeclare?: (task: Task, state: string, was: string) => void;
  /** Start a session on a task: opens the launch sheet with it picked (--task-id). Shown on open, doing and blocked tasks, never on ideas. */
  onSession?: (task: Task) => void;
  /** Run a team on a task: opens Start a run with it picked (--task). Disabled when a team run already has it in doing. */
  onRun?: (task: Task) => void }): { el: HTMLElement; set: (tasks: Task[]) => void };
/** What a session would start with: one bar of its specialists against the token budget, why each is there, and what was left out. */
export declare function LoadStack(o: {
  selected: { id: string; title: string; reason?: string; estimatedTokens: number; always?: boolean }[];
  omitted?: { id: string; reason: string }[]; conflicts?: number;
  estimatedTokens?: number; tokenBudget?: number; nearBudget?: boolean;
  href?: (id: string) => string; onPick?: (id: string) => void;
}): HTMLElement;
/** Builds "$ loadout <path> …" the way the drawers do. */
export declare function commandLine(path: string, fields: DrawerField[], values: Record<string, string | boolean>, extra?: string[]): string;
export declare const TASK_NEXT: Record<string, string[]>;
export declare const TASK_MOVE: Record<string, string>;
/** 1.2k, 3.4M, 1.1B. */
export declare function tokens(n: number): string;

export interface ConfigSetting { name: string; value: string; description: string; machineLocal: boolean; flag: boolean; whenUnset: string | null }
/** Every setting `loadout config` knows, in its groups: found by name or what it does, and changed where it is shown with `loadout config set`. */
export declare function ConfigList(o: { groups: { group: string; settings: ConfigSetting[] }[]; disabledReason?: string; from?: boolean; onSet?: (name: string, value: string) => string | void; onExplain?: (setting: ConfigSetting) => string }): { el: HTMLElement; find: (query: string) => void };
/** A project's memory by store (This project, Your work, This machine only), and what the agent recorded outside the workspace. */
export declare function StoreList(o: { stores: { key: string; label: string; hint?: string; readOnly?: boolean; topics: { name: string; description?: string; facts?: number }[] }[]; picked?: { store: string; name: string }; href?: (store: string, name: string) => string; agent?: { command: string; wouldImport?: string[]; drifted?: string[]; error?: string } | null }): HTMLElement;
export interface MemoryTopic { name: string; scope: string; description?: string; kind?: string; written?: string; version: string; text: string }
/** One topic, read and edited in place. A save carries the version it was read at; a topic changed meanwhile is shown beside your edit. */
export declare function TopicEditor(o: { project: string; topic: MemoryTopic; storeLabel?: string; editable: boolean; readOnlyReason?: string; onSave?: (text: string, version: string) => { saved: MemoryTopic } | { changed: MemoryTopic }; onSaved?: (topic: MemoryTopic) => void }): { el: HTMLElement; readonly draft: string; readonly dirty: boolean };
export interface UsageTotals { input: number; output: number; thinking: number; cacheRead: number; total: number; billedInputEquivalent: number; savedFraction: number; cacheHitFraction: number }
/** What the agents spent, billed as input, what the cache saved, and how it splits. */
export declare function TokenSplit(o: { totals: UsageTotals; since?: string; complete?: boolean; from?: string }): HTMLElement;
/** Tokens by project, model or agent as bars (the twenty largest); by day as columns read with the arrow keys. */
export declare function SpendBars(o: { groups: { name: string; value: number; registered?: boolean }[]; by: "project" | "day" | "model" | "agent"; limit?: number; label?: string; unit?: string; format?: (n: number) => string }): HTMLElement;

/* ---------- The six pages in every mode (stage 9) ---------- */

/** A page opened beside the terminals (Terminal Focus) or the runs board (Team Focus), which stay live. Escape or Close closes it, unless focus is in a field, a terminal or a dialog. */
export declare function PageBeside(o: { page: string; beside?: string; content?: Node; closeLabel?: string; onClose?: () => void }): { el: HTMLElement; body: HTMLElement; set: (content: Node | null) => void; close: () => void; destroy: () => void };
/** What you are watching and the page beside it. Below 1100px the page takes the width. */
export declare function BesideGrid(focus: Node, beside: ReturnType<typeof PageBeside> | HTMLElement): HTMLElement;
/** A page in Minimal: one sentence, what needs you or matters now (a QuietList), and Show everything for the rest. */
export declare function QuietPage(o: {
  title: string; sentence?: string | Node;
  items: { id: string; kind?: string; title: string; word: string; where?: string; ask?: string }[];
  onOpen?: (item: unknown, from: DOMRect) => void;
  /** Built the first time Show everything is pressed. */
  everything?: () => Node;
  calmText?: string; moreLabel?: string; lessLabel?: string; label?: string; open?: boolean; onToggle?: (open: boolean) => void;
}): { el: HTMLElement; showAll: () => void };
/** Forget a project on this machine only (`project remove <p>`) or from the Loadout workspace for every machine (`--from-workspace`, pressed twice), with optional cleanup that runs first: Git protections, its tasks, ideas and memory topics. The repository is never touched. Put it in a Dialog. */
export declare function ForgetProject(o: {
  project: { id: string; name?: string; available?: boolean };
  counts?: { tasks?: string[]; ideas?: string[]; topics?: string[] };
  definition?: { path: string; files: number };
  disabledReason?: string;
  onRun?: (plan: { scope: "here" | "everywhere"; lines: string[]; preview: boolean; picked: Record<string, boolean> }) => string | void;
}): { el: HTMLElement; readonly scope: "here" | "everywhere"; steps: () => string[] };

/* ---------- Team run progress, Red Alert, tasks and value (stage 10) ---------- */

export type Verdict = "met" | "unmet" | "not-attempted" | "not attempted" | null;
/** "3 of 5 met, up from 1 · last moved 2 rounds ago", with a small line of the met count by round (decoration; the words carry it). For run cards and the board. */
export declare function ProgressLine(o: { total: number; byRound?: number[]; met?: number; lastMoved?: number | null; stalled?: boolean; compact?: boolean }): HTMLElement;
/** Each done-when a row, each round a column, met / unmet / not attempted / no verdict as glyph and word; what changed in a round is tinted. For a run's page. */
export declare function CriteriaGrid(o: { rounds: number; current?: number; criteria: { text: string; verdicts: Verdict[] }[]; stalledFor?: number; from?: string }): HTMLElement;
/** Picking a finished run up: asks for exactly what it ran out of (`team resume --usd` in all from now on, `--rounds` more), and an optional message. */
export declare function PickUpSheet(o: { run: { id: string }; ended: "budget" | "rounds" | "other"; spent?: number; cap?: number; rounds?: number; roundLimit?: number; reason?: string; disabledReason?: string; onSubmit?: (command: string, values: { usd: string; rounds: string; message: string }) => string | void }): { el: HTMLElement; command: () => string };
/** What a run was refused, allowed and decided, from its journal, with a Refused filter. */
export declare function RefusalsList(o: { items: { at?: string; outcome: "refused" | "allowed" | "answered" | "merged"; what: string; detail?: string }[]; only?: "all" | "refused"; from?: string }): HTMLElement;
/** When enabled and something needs you, the page's edge lights (Needs, or Bad for a problem) and one line says what, with Go to it and Quiet until something new. Clears itself. Breathes under full motion; steady under reduced; a still border under none and in Pure Focus. */
export declare function RedAlert(o: { enabled?: boolean; host?: HTMLElement; onGo?: (item: { id: string; text: string; level: "needs" | "problem" }) => void }): { el: HTMLElement; set: (items: { id: string; text: string; level: "needs" | "problem" }[]) => void; enable: (on: boolean) => void; readonly showing: boolean; destroy: () => void };
/** Which recorded task this is for: doing, then open, then blocked, newest first; or something else. */
export declare function TaskPicker(o: { tasks: { id: string; title: string; state: string; declaredBy?: string; declared?: string; note?: string | null }[]; selected?: string | null; label?: string; otherLabel?: string; otherSub?: string; onPick?: (task: { id: string; title: string; note?: string | null } | null) => void }): HTMLElement;
/** What Loadout did, counted, never estimated. A figure that is not recorded yet (value null) says so. */
export declare function ValueReport(o: { head?: string | Node; groups: { name: string; figures: { label: string; value: number | string | null; detail?: string; from?: string; waits?: string }[] }[]; how?: string }): HTMLElement;

/* ---------- The field map (stage 11) ---------- */

export type Backdrop = "plain" | "map" | "map-animated";
/** Set data-backdrop on <html>: the plain ground, the field map still (the default), or the field map with its scan. */
export declare function setBackdrop(value: Backdrop, el?: Element): void;
export declare const BACKDROPS: Backdrop[];
/** The embossed survey map behind every page: contour lines (every fifth heavier), grid squares and map references, the same map on every visit. Still unless data-backdrop is map-animated and motion is full and the mode isn't Pure; hidden for plain and both high-contrast themes. Mount once. */
export declare function FieldMap(o?: { seed?: number; scale?: number; levels?: number; cell?: number; square?: number; host?: HTMLElement; width?: number; height?: number }): { el: HTMLElement; redraw: () => void; destroy: () => void };

/* ---------- The office (stage 12) ---------- */

/** Something in the office the keyboard can stop on: a floor from outside, or a person or room inside. Its rect is in CSS pixels inside the stage. */
export interface OfficeTarget { id: string; kind: "floor" | "level" | "person" | "room"; label: string; detail?: string; run?: string; room?: string; rect: { x: number; y: number; w: number; h: number } }
export interface OfficeWhere { place: "outside" | "lobby" | "roof" | "basement-1" | "basement-2" | "floor"; label: string; detail?: string; view?: "corner" | "aerial" | "street"; canTurn: boolean; canLevel: [boolean, boolean]; zoom: number; zoomRange: [number, number] }
/**
 * What the shared office engine (the classic dashboard's, moved to its own file) offers a host page.
 * It draws exactly as it does today. The only new things are where() and targets(), so the host can draw a
 * focus ring and move between things by keyboard, and activate(), which does what a click does.
 */
export interface OfficeEngine {
  mount(stage: HTMLElement, o: { motion: () => "full" | "reduced" | "none"; onChange: () => void }): void;
  where(): OfficeWhere;
  targets(): OfficeTarget[];
  /** A floor from outside: goes in ("entered"). A person or a room: "picked", and the host shows its panel. */
  activate(id: string): "entered" | "picked";
  /** Out a level (a floor to the building); false when already outside. */
  back(): boolean;
  turn(d: -1 | 1): void; zoom(d: -1 | 1): void; level(d: -1 | 1): void; setView(v: "corner" | "aerial" | "street"): void;
  resize?(): void; destroy(): void;
}
/** Studio's frame round the office: where you are, the toolbar (back, levels, outside views, turn, zoom), the picture with a keyboard focus ring, a side for panels, and the keys. */
export declare function OfficeView(o: { engine: OfficeEngine; label?: string; settingsHref?: string; onPick?: (t: OfficeTarget) => void }): { el: HTMLElement; stage: HTMLElement; side: HTMLElement; show: (panel: HTMLElement) => void; hide: () => void; refresh: () => void; focus: (id?: string) => void; destroy: () => void };
export interface OfficeAction { label: string; cmd?: string; danger?: boolean; ask?: string; yes?: string; said?: string; disabledReason?: string; onPress?: () => void }
/** A room or a person: what it is, what's going on in it, the classic popup's actions as Studio buttons with their commands, and Name this room for a run's own room. */
export declare function OfficePanel(o: { kind?: string; title: string; lines?: string[]; list?: string[]; teams?: { name: string; list?: string[]; open?: () => void }[]; actions?: OfficeAction[]; rename?: { run: string; current?: string; worked?: string; onSave?: (name: string) => void }; onClose?: () => void }): HTMLElement;
