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
  /** Pressing does nothing, with no reason to give: "already first" is said by the page. Announced as disabled. */
  inactive?: boolean;
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

/** background: "waiting on background work", quiet on purpose (no ask, no badge, no count); it turns into waiting for you by itself if the work goes silent past its time. */
export type TerminalState = "connecting" | "working" | "idle" | "unknown" | "waiting" | "approval" | "background" | "ended" | "error" | "reconnecting" | "watch" | "lost";
/** One line of output as runs of [text, classes]: fg-<ansi>, fg-bright-<ansi>, bg-black, bold, ul. */
export type TerminalRun = [string, string?];
export interface TerminalOptions {
  /** sessionId: the agent's own id for the conversation, for the resume line ("$ loadout resume <id> --project <project>", or --last, or --all where the project is not known). */
  title?: string; project?: string; agent?: string; label?: string; sessionId?: string;
  state?: TerminalState; size?: [number, number]; fit?: boolean; prompt?: boolean;
  lines?: (TerminalRun[] | string)[];
  /** Where Ctrl+] sends focus. Not given, the terminal raises a cancelable "leaveterminal" event (TermDeck takes it to the tab), and failing that the next control on the page. */
  leaveTo?: HTMLElement;
  onInput?: (text: string) => void; onRetry?: () => void; info?: { code?: number };
  /** A row on the terminal's foot, under the screen: the session's StatusStrip (a node, or anything with .el). */
  foot?: Node | { el: HTMLElement } | null;
}
/** The live terminal's reference look: bar, screen and the bar that says what the session needs. Build the real one on xterm.js themed from the term-* tokens. */
export declare function Terminal(o: TerminalOptions): { el: HTMLElement; write: (runs: TerminalRun[] | string) => void; setState: (s: TerminalState, info?: { code?: number }) => void; /** Puts a row on the foot, or takes it away with null. */ setFoot: (node: Node | { el: HTMLElement } | null) => void; focus: () => void; readonly state: TerminalState };
/** The line that carries a session on in a terminal: "$ loadout resume <agentSession> --project <project>", --last where no conversation is known, --all where no project is. */
export declare function resumeCommand(agentSession?: string | null, project?: string | null): string;
export declare const TERMINAL_STATES: TerminalState[];

/** The session page: header, the terminal filling the page, and a side panel that folds away. */
export declare function SessionView(o: {
  line: { title: string; where?: string; agent?: string; mode?: string; time?: string };
  terminal?: TerminalOptions;
  facts?: [string, string][]; loaded?: [string, string][]; events?: [string, string][];
  actions?: HaloButtonOptions[]; details?: boolean;
  /** In Actions, before the buttons: the commands that carry it on elsewhere ("Started with:", "Carry this conversation on in a terminal instead:"). */
  commands?: { why?: string; command: string }[];
  /** In Actions, under the commands: "Sessions survive a reload of this page, not a restart of the daemon." */
  note?: string;
  /** The session's status, drawn by StatusBlock after Facts, where main puts it; returned as statusBlock for set(). */
  status?: SessionStatus; now?: number;
  /** More side sections: a node, anything with .el, or { title, content }; at "facts" (after Facts, the default), "loaded", "events" or "actions" (after Actions). */
  sections?: (Node | { el: HTMLElement; at?: "facts" | "loaded" | "events" | "actions" } | { title: string; content: Node | Node[]; at?: "facts" | "loaded" | "events" | "actions" })[];
  /** A button in the header before Details, e.g. "Type here instead" when another window holds the input. */
  button?: HaloButtonOptions | Node;
}): { el: HTMLElement; terminal: ReturnType<typeof Terminal>; side: HTMLElement; head: HTMLElement; statusBlock: ReturnType<typeof StatusBlock> | null; /** Puts the header button, or takes it away with null. */ setButton: (b: HaloButtonOptions | Node | null) => void };

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

export interface DeckSession { id: string; title: string; word: string; where?: string; agent?: string; unread?: boolean;
  /** Whether it has ended: closing its tab then clears it rather than asking. */
  ended?: boolean;
  /** What it was launched as, for going back to after a rename. */
  launchTitle?: string;
  /** Whether its agent is known to be given its name as well. */
  namesAgent?: boolean;
  /** "launch" for an agent session (the default), "command" for a command that asks as it goes. */
  kind?: "launch" | "command";
  /** The project it runs in, for saying what ending it ends. */
  project?: string | null;
  /** How much of its context window is in use, drawn as a ContextMark; null where its status line has not said. */
  context?: number | null;
  /** The daemon's id for it, for the rename and end commands; defaults to id. */
  hid?: string }
/** Every session as a tab with its state, its context, Ctrl+Alt+n on the first nine, and a new-output dot. Each tab closes (an ended one clears; a running one asks Hide or End, and End asks again), renames in place (F2), moves (drag, Ctrl+Shift+arrows) and has a menu (right-click, the context-menu key, Shift+F10). Hidden tabs come back from "N hidden". Each change is made at once and told to its callback. */
export declare function SessionTabs(o: { sessions: DeckSession[]; current?: string; controls?: string; onPick?: (id: string) => void; onMaximise?: (id: string) => void; onNew?: () => void; label?: string;
  /** Running sessions this browser took off the strip. */
  hidden?: DeckSession[];
  /** false where Studio is only watching: no Rename and no End. */
  changes?: boolean;
  /** The strip's new order, every tab on it. */
  onMove?: (order: string[]) => void;
  /** A new name, or null for the one it was launched as ("$ loadout session rename <id> <name>|--reset"). */
  onRename?: (id: string, name: string | null) => void;
  onHide?: (id: string) => void; onUnhide?: (id: string) => void; onClear?: (id: string) => void;
  /** Ended after its second ask ("$ loadout session end <id> --yes"). */
  onEnd?: (id: string) => void }): HTMLElement & { /** The page's word again; a rename in progress is kept. */ update: (p: { sessions?: DeckSession[]; hidden?: DeckSession[]; current?: string | null; changes?: boolean }) => void; rename: (id: string) => void; closeMenu: () => void };
/** Terminal Focus: tabs over one, two or four panes of Terminal, a find bar, and the keys. The deck makes no terminals itself: terminal(id) does. */
export declare function TermDeck(o: { sessions: DeckSession[]; current?: string; layout?: 1 | 2 | 4; terminal: (id: string) => ReturnType<typeof Terminal>; onPick?: (id: string) => void; onLayout?: (n: number) => void; onNew?: () => void; onShown?: (ids: string[], terms: Record<string, ReturnType<typeof Terminal>>) => void;
  /** Running sessions this browser took off the strip, offered back from "N hidden". */
  hidden?: DeckSession[];
  /** false where Studio is only watching: no Rename and no End. */
  changes?: boolean;
  /** How many panes show where a launch grew past the layout chosen, kept by the page. */
  grown?: 2 | 4 | null;
  /** Which session each pane showed last time, kept by the page. */
  slots?: (string | null)[];
  /** Whether the sessions have been read yet; before then, none in a pane is said to have gone. Default true. */
  listed?: boolean;
  /** A session just started: it takes an empty pane, growing 1→2→4, else the pane of one ended or idle longest; said once beside Find. */
  launch?: string | null;
  /** When each session last wrote or was typed into (ms), for which pane gives way to a launch. */
  active?: Record<string, number>;
  /** Where the dividers were left: for two panes [column], for four [column, row]. */
  splits?: { 2?: number[] | null; 4?: number[] | null };
  onLaunched?: (id: string) => void; onGrown?: (panes: 1 | 2 | 4) => void; onSlots?: (slots: (string | null)[]) => void;
  /** A divider moved, for the layout showing. */
  onSplit?: (layout: 2 | 4, fractions: number[]) => void;
  onMove?: (order: string[]) => void; onRename?: (id: string, name: string | null) => void; onHide?: (id: string) => void; onUnhide?: (id: string) => void; onClear?: (id: string) => void; onEnd?: (id: string) => void;
  label?: string }): { el: HTMLElement; tabs: ReturnType<typeof SessionTabs>; pick: (id: string, focus?: boolean) => void; setLayout: (n: 1 | 2 | 4) => void; terminal: (id: string) => ReturnType<typeof Terminal> | undefined;
  /** The daemon's list again, or a patch; current names a session to show, launch one just started. */
  update: (p: DeckSession[] | { sessions?: DeckSession[]; hidden?: DeckSession[]; changes?: boolean; listed?: boolean; active?: Record<string, number>; current?: string; launch?: string | null }) => void;
  /** A session just started: placed now if it is among the tabs, else once an update brings it. */
  launch: (id: string) => void;
  readonly current: string; readonly layout: number; readonly chosen: number; readonly slots: (string | null)[] };

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


/* ---------- What main's Studio built since the design (stage 14) ---------- */
/** What 'loadout ask --json' answers (main's ask.ts AskReply). Properties --json leaves out may be missing. */
export interface AskReply {
  query: string;
  kind: "answer" | "plan" | "unplaced";
  answeredBy?: "loadout" | "agent";
  summary: string;
  spend?: { models: { model: string; tokens: number; usd?: number | null }[]; caveat?: string | null; usd?: number; basis?: string } | null;
  tasks?: { project: string; id: string; title: string; score: number; reasons: string[] }[];
  plan?: AskPlan | null;
  nearMisses?: { shape: AskPlan["shape"]; rewrite: string }[];
  /** What asking the agent instead would cost, roughly, when nothing was placed. */
  agentOffer?: { estimatedUsd: number; basis: string } | null;
  /** What the agent's turn cost, when it answered. Never part of a plan's cap. */
  agentCostUsd?: number | null;
  agentSession?: string | null;
}
/** One request and what came back, or why nothing did (main's ask.ts AskTurn). reply and failed both null: still asking. */
export interface AskTurn { id: number | string; query: string; reply: AskReply | null; failed: string | null }
/** The palette's conversation with Loadout (Ask Loadout, '?' in the CommandPalette): each request and its answer, newest last. An answer says who gave it (Loadout or the agent, and what the agent cost); spend by model; tasks with why; a plan as an AskPlanCard; near misses to ask as they stand, and the agent instead, for about its cost. */
export declare function AskPane(o: {
  turns: AskTurn[];
  /** A request is out: Ask the agent and Clear the conversation are held. */
  asking: boolean;
  /** Asks a near miss as it stands. */
  onrewrite: (rewrite: string) => void;
  /** Asks the agent the request as it was typed. */
  onagent: (query: string) => void;
  /** Carries a plan out; passed to each AskPlanCard. */
  approve: (plan: AskPlan, team: string | null, cap: string) => string | Promise<string>;
  onclear: () => void;
}): {
  el: HTMLElement;
  /** New turns or asking; a turn already shown keeps its nodes (and its plan card what was typed into it). */
  set: (p: { turns?: AskTurn[]; asking?: boolean }) => void;
  /** Main's AskConversation: adds the request at once and its reply, or why there was none, when asker settles. */
  ask: (query: string, asker: (query: string) => AskReply | Promise<AskReply>) => Promise<void>;
  clear: () => void;
};
/** One step of a plan from 'loadout ask --json' (main's ask.ts AskStep). */
export interface AskStep { kind: "queue-task" | "start-team" | "open-terminal"; project: string | null; task: string | null; says: string; reasons: string[] }
/** A plan from Ask Loadout: what it would queue, start or open, carried out only when approved (main's ask.ts AskPlan). */
export interface AskPlan {
  shape: "spend" | "tasks" | "team-queue" | "terminals";
  /** The team it names, or null: then the card asks for one from teamChoices. */
  team: string | null;
  teamChoices: string[];
  steps: AskStep[];
  /** Whether the plan carries a spending cap. */
  capped: boolean;
  /** The cap it starts with, from team-budget, or null. */
  cap: number | null;
  /** team-budget says 'none': no cap, on purpose. */
  capNone: boolean;
  /** What was left out, and why ("t-19 is blocked"). */
  left: string[];
  needs: string[];
}
/** One plan from Ask Loadout: every step with why its task was picked, the team and the cap, and Approve with the command it runs ("$ loadout team queue run-now …"). Approve is held, saying why, while a team or a needed cap is missing. */
export declare function AskPlanCard(o: {
  /** The plan as 'loadout ask --json' writes it; what --json leaves out is filled in. */
  plan: AskPlan;
  /** Carries the plan out and says what happened, or throws (rejects) with why not. */
  approve: (plan: AskPlan, team: string | null, cap: string) => string | Promise<string>;
}): { el: HTMLElement; plan: AskPlan };
export type BlockedAction = "carry-on" | "raise" | "requeue" | "edit-requeue" | "set-aside" | "drop";
/** A run that ended short of its done-when, as `loadout team blocked list --json` reads it (main's lib/blocked.ts BlockedRead). */
export interface BlockedRead {
  id: string; team: string; project: string | null; task: string | null; goal: string;
  kind: "blocked" | "failed" | "limited" | "unrecorded" | "dead" | "handover";
  /** How it said it ended, or null. */
  ended: string | null;
  cost: number; budget: number | null; rounds: number; roundLimit: number;
  /** The trigger rule that started it, or null. */
  origin: string | null; follows: string | null;
  /** How many runs in a row on its task, this one included, ended without getting there. */
  chain: number;
  merged: string[]; unmerged: string[];
  /** What its lead last said, or null. */
  lead: { blocker: string | null; unblockedBy: string | null; questions: string[]; next: string | null; summary: string | null } | null;
  suggested: { action: BlockedAction; label: string; reason: string; rule: string };
  /** Every action open to it, the suggestion first. */
  offered: { action: BlockedAction; label: string }[];
  /** For a limited run: what it ran out of, filled in as the same again. */
  raise: { cap: "rounds" | "budget"; figure: number; used: number; said: string } | null;
}
/** What a person filled in before confirming. */
export interface BlockedChoice { action: BlockedAction; answer: string; figure: string; title: string; detail: string; doneWhen: string }
/** Runs that ended short of their done-when, each with Loadout's suggestion and why, and every other action. Nothing is done until Confirm, which shows its `loadout team blocked resolve` line. */
export declare function BlockedRuns(o: {
  runs: BlockedRead[];
  /** When set, Confirm does nothing and shows this instead of its command. */
  disabledReason?: string;
  /** Resolves to what the command said, or throws (rejects) with why it refused; either is shown under the run. */
  onresolve: (run: BlockedRead, choice: BlockedChoice) => Promise<string> | string;
}): { el: HTMLElement; /** A reread: choices already made are kept by run id. */ set: (patch: Partial<{ runs: BlockedRead[]; disabledReason: string; onresolve: (run: BlockedRead, choice: BlockedChoice) => Promise<string> | string }>) => void };
/** How much of a session's context window is in use, for a tab or a list row: the figure and a short beam, Needs past 70% and Bad with "!" past 90%. role="img" with the words in its label. Hidden, with nothing drawn, while the percent is null. */
export declare function ContextMark(o: {
  /** The share in use, rounded; null where Claude has not said. */
  percent: number | null;
}): { el: HTMLElement; set: (percent: number | null) => void };
/** What a conversation was launched for, when Loadout launched it (main's api.ts LaunchedFor). */
export interface LaunchedFor { launch: string; task: string | null; taskId: string | null; mode: string | null; profile: string | null; worktree: string | null }
/** A conversation from either agent's own history, as 'loadout sessions' lists it (main's api.ts Conversation). */
export interface Conversation {
  agent: string; id: string; title: string;
  /** Null when it ran in a folder that is not a project. */
  project: string | null;
  /** Whether its folder is a registered project; if not, Resume is "Register and resume". */
  registered: boolean;
  directory: string; branch: string | null;
  /** ISO time. */
  lastActive: string;
  /** Null where there is no link: nothing is carried over, and nothing is guessed. */
  launchedFor: LaunchedFor | null;
  /** none, elsewhere, or hosted:<session id>. */
  running: string;
}
/** Past conversations from both agents, each with where and when, what would carry over and whether it is running, and Open (in a Studio terminal) or Resume ("$ loadout resume <id> --project <slug>"). Used on Sessions and a project's page. */
export declare function ConversationList(o: {
  conversations: Conversation[];
  /** The list's accessible name ("Past conversations"). */
  label: string;
  /** Offers "Resume with changes…" on a registered one: opens the launch sheet filled in with what would carry over. */
  onchanges?: (conversation: Conversation) => void;
  /** Leaves the project out of each row, where the page is already about one. */
  inProject?: boolean;
  /** Resumes it (main: studio.resume): resolves to the new session's id, or null when declined. One at a time. */
  onresume?: (conversation: Conversation) => string | null | Promise<string | null>;
  /** Goes to a session's terminal: one already open (launched false), or one just resumed (launched true). */
  onopen?: (session: string, launched: boolean) => void;
  /** Studio is only watching: Resume is held, saying watchOnlyReason. */
  watchOnly?: boolean;
  watchOnlyReason?: string;
  /** False when the daemon cannot open a terminal here: Resume is held, saying so. */
  startsSessions?: boolean;
  /** The clock "4 min ago" is worked out from, in ms or ISO. Default now. */
  now?: number | string;
}): { el: HTMLElement; set: (p: Partial<{ conversations: Conversation[]; label: string; inProject: boolean; watchOnly: boolean; startsSessions: boolean; now: number | string }>) => void };
/** One thing a run handed over, as the daemon's run read gives it (handover.items). */
export interface HandoverItem {
  /** The done-when criterion it could not meet itself. */
  criterion: string;
  /** "needs-push", "needs-ci" or "needs-person". */
  needs: string;
  /** What to do, in the run's words. */
  todo: string;
  /** Who ticked it off, or null while it is still yours. */
  checkedBy: string | null;
}
/** What a run handed to you because only you could do it. Each is ticked off once done, kept as review evidence in your name; the last tick judges the run again. */
export declare function Handover(o: {
  /** The run's id, for the command. */
  run: string;
  items: HandoverItem[];
  /** Where the work is: "The work is on …". */
  branches?: string[];
  /** When set, the tick buttons do nothing and show this instead of their command. */
  disabledReason?: string;
  /** Ticks one off, by its number (from 1). While its promise is out, the other buttons say "ticking one off". Runs `loadout team handover <run> --check <n>`. */
  oncheck?: (number: number) => Promise<boolean | void> | boolean | void;
}): { el: HTMLElement; /** New items (with checkedBy filled in), branches or reason. */ set: (patch: Partial<{ items: HandoverItem[]; branches: string[]; disabledReason: string; oncheck: (number: number) => Promise<boolean | void> | boolean | void }>) => void };
/** One command run from Studio, as the daemon serves it (main's api.ts Job). */
export interface StudioJob {
  id: string;
  /** The command's path, e.g. "task list". */
  path: string;
  /** The line it ran, e.g. "loadout task list --project loadout-cli --json". */
  line: string;
  state: "running" | "done" | "failed" | "cancelled";
  exitCode: number | null;
  startedAt: string;
  endedAt: string | null;
  /** It said more than Studio keeps. */
  truncated: boolean;
  output: string | null;
  error: string | null;
}
/** One command run from Studio: its state as a Signal, when it started, how long it took and its exit code, the line it ran, and once it ends what it said (JSON pretty-printed) and what it said on standard error. Cancel while it runs; Run it again once it ends. */
export declare function JobResult(o: {
  /** The job; nothing is drawn until there is one. */
  job?: StudioJob | null;
  /** Under a form, without the command's name as a heading or its line. */
  compact?: boolean;
  /** Offered once it has ended, as Run it again. */
  onagain?: (job: StudioJob) => void;
  /** Cancel while it runs (main: DELETE /api/v1/jobs/<id>). A rejected promise is shown as the problem. */
  oncancel?: (job: StudioJob) => void | Promise<unknown>;
  /** Shown in place of Cancel's command, and Cancel does nothing, e.g. when Studio is only watching. */
  disabledReason?: string;
  /** Reading the job failed: shown as an alert above it. */
  problem?: string | null;
}): { el: HTMLElement; set: (p: { job?: StudioJob | null; problem?: string | null; compact?: boolean; disabledReason?: string; onagain?: (job: StudioJob) => void; oncancel?: (job: StudioJob) => void | Promise<unknown> }) => void; /** The job read again. */ update: (job: StudioJob) => void };
/** One line of a run's journal or a node's stream, as main's lib/teams.ts JournalLine. */
export interface JournalEntry {
  /** "14:02", or "" for a line that is not an event. */
  time: string;
  text: string;
  /** Signal family that colours the words: "needs", "bad" or "good". The kind word says it too. */
  family?: string;
  /** What kind of line: "round", "gate", "said", "bash". */
  kind?: string;
  /** Said by a sub-agent the node started: indented. */
  sub?: boolean;
  /** How many times a repeat came round: "×4". */
  repeats?: number;
}
/** A run's journal or a node's stream, one line each: when, what kind, what was said, and how many times a repeat came round. Styled inside .ls-depths, where main draws it. */
export declare function Journal(o: {
  lines: JournalEntry[];
  /** The list's accessible name. Default "Journal"; a node's stream: "What implementer/1 did". */
  label?: string;
}): { el: HTMLOListElement; /** New lines replace the old; a new label renames it. */ set: (patch: { lines?: JournalEntry[]; label?: string }) => void };
/** A field a list can be filtered or sorted on, as the daemon reports it. */
export interface ListField { name: string; type: "text" | "choice" | "date" | "number"; description: string; choices: string[] }
/** What the daemon made of a list's query: its pieces, the fields and the default (main's lib/lists.ts ListShape). */
export interface ListShape { text: string; parts: string[]; fields: ListField[]; defaultQuery: string }
/** The same search, filter and sort controls over every list. The box, the chips and the menus all edit the one query string the daemon read; '/' puts you in the box. */
export declare function ListToolbar(o: {
  /** What the list is, for the box's label: "tasks". */
  noun: string;
  /** The query as it stands. */
  query: string;
  /** What the daemon made of it, or null before the first read. */
  shape: ListShape | null;
  /** How many the query kept, and how many there were before it: "Showing 2 of 7". */
  shown?: number | null;
  recorded?: number | null;
  /** What the daemon refused about the query, if it did: an alert under the box. */
  problem?: string | null;
  /** Whether '/' comes here. Only one toolbar on a page should take it. Default true. */
  slash?: boolean;
  /** The new query, trimmed: on Enter, after a 350ms pause in typing, or from a chip, menu, reset or Show all. Read the list and call set. */
  onchange: (query: string) => void;
}): { el: HTMLElement; /** What the daemon said back; a new query replaces what is in the box. */ set: (patch: Partial<{ noun: string; query: string; shape: ListShape | null; shown: number | null; recorded: number | null; problem: string | null; slash: boolean }>) => void; focus: () => void; /** Stops listening for '/'. */ destroy: () => void };
/** A run's rounds as a line small enough for a board row. Decoration (aria-hidden): the row beside it says the same in words. */
export declare function MiniLine(o: {
  /** Each round's word ("done", "running", "waiting for you", "failed") and its nodes as [name, word]. */
  rounds: { word: string; nodes?: [string, string][] }[];
  /** A solid stop where it ended. */
  end?: boolean;
  /** A dashed stop for what comes next. Wins over end. */
  running?: boolean;
}): { el: HTMLElement; set: (patch: Partial<{ rounds: { word: string; nodes?: [string, string][] }[]; end: boolean; running: boolean }>) => void };
/** A ring that fills for stages, the Meter's round form. The figure in it is decoration: say the stage in words beside it. */
export declare function ProgressRing(o: {
  /** How far, from 0 to 1; clamped. */
  value: number;
}): { el: HTMLElement; /** A new value: a number, or { value }. */ set: (next: number | { value: number }) => void };
export interface QueueItemRow { position: number; task: string; project: string; after?: string | null; by?: string; added?: string; refused?: string | null }
export interface QueueRunRow { task: string; project: string; run?: string | null; state: string; reason?: string | null; costUsd: number; started: string; ended?: string | null; by?: string }
export interface QueueSettingsRow {
  windowUsd?: string | null; windowRuns?: number | null; heldCap: number; draftPullRequests: boolean;
  /** draft, ready or none; absent when the queue leaves it to each project. */
  prMode?: string | null;
}
export interface QueueWindowRow { at: string | null; lengthMinutes: number; autonomy: string; open: boolean; next?: string | null }
/** What 'team queue list <team> --json' says. */
export interface QueueRead {
  team: string; items: QueueItemRow[]; runs: QueueRunRow[]; settings: QueueSettingsRow;
  /** When the daemon works through it on its own, or null when only by hand. */
  window?: QueueWindowRow | null;
  /** When the window being worked in opened, while one is. */
  windowOpened?: string | null;
}
/** What 'team queue plan <team> --json' says. */
export interface QueuePlan { order: { task: string; project: string }[]; left: { task: string; project: string; reason: string }[]; stops: string }
/** What 'task propose-done-when --json' says: an agent's proposal, recorded nowhere yet. */
export interface DoneWhenProposed { task: string; project: string | null; proposed: string[]; why: string; costUsd: number; replaces: string[]; recorded: boolean }
export interface QueueTask {
  id: string; title: string; state: string; kind: string;
  /** Its finish line; a task without one is refused by the queue. */
  doneWhen?: string[];
}
export interface QueuePanelOptions {
  /** The team's queue, as 'team queue list <team> --json' reads it. */
  queue: QueueRead;
  /** The projects a task can be queued from, by slug. */
  projects: string[];
  /** The tasks on a project's list that could be queued ('task list --project <slug>'). */
  tasksOf: (project: string) => Promise<QueueTask[]>;
  /** Asks an agent for a task's done-when; records nothing. Without it, Propose them is not offered. */
  propose?: (project: string, task: string) => Promise<DoneWhenProposed>;
  /** Runs one queue command ('team queue window|settings|move|remove|run-next|add', 'task declare') and says whether it was done: false keeps the form open. */
  act: (path: string, words: string[], done: string) => Promise<boolean | void> | boolean | void;
  /** Reads what the queue would run next. */
  readPlan: () => Promise<QueuePlan>;
  /** Why nothing can be pressed, when Studio is only watching. Shown in place of each command. */
  disabledReason?: string;
  /** For a day's spend, so tests can fix the clock. */
  now?: () => number;
}
/** One team's queue: what waits and what would be refused, the pull request grant said first, its window, its limits and what it ran. */
export declare function QueuePanel(o: QueuePanelOptions): {
  el: HTMLElement;
  /** A new read of the queue, or new props; open forms and what is typed in them are kept. */
  set: (o: Partial<QueuePanelOptions>) => void;
};
export declare namespace QueuePanel {
  /** A team's name as an owner: builders' and docs-crew's. */
  function whose(team: string): string;
}
/** The spend line for a run: what it has spent, of what, and where it is projected to end. Past its budget is said in words with a Needs edge. */
export declare function Spend(o: {
  /** Dollars spent so far. */
  spent: number;
  /** Its budget in dollars, or null for none. */
  cap?: number | null;
  /** No cap, on purpose: drawn as words, no bar. */
  uncapped?: boolean;
  /** Where it is projected to end, in dollars: a mark on the bar, or words when past the cap. */
  projected?: number | null;
  /** "$0.41 a round". */
  rate?: string | null;
}): { el: HTMLElement; /** New figures, merged into the old. */ set: (patch: Partial<{ spent: number; cap: number | null; uncapped: boolean; projected: number | null; rate: string | null }>) => void };
/** Where a column divider and a row divider cross: dragging moves both, a double click puts both back. Pointer only: aria-hidden and takes no focus, since the two Splitters either side take the keys. */
export declare function SplitCross(o: {
  /** Where the column boundary sits, as a fraction of the width the panes share. */
  column: number;
  /** Where the row boundary sits, as a fraction of the height the panes share. */
  row: number;
  /** Defaults 0.1 and 0.9 each. */
  colMin?: number; colMax?: number; rowMin?: number; rowMax?: number;
  /** Where a double click puts [column, row]. */
  reset: [number, number];
  /** Called with both new fractions. */
  onchange: (column: number, row: number) => void;
}): { el: HTMLElement; set: (p: { column?: number; row?: number; colMin?: number; colMax?: number; rowMin?: number; rowMax?: number; reset?: [number, number] }) => void };
/** The divider between two panes of a split: role="separator", laid over its grid's gap. Drag it; arrows move it 2%, Shift+arrow 10%, Home and End to its limits, Enter or a double click back to reset. Raises "ls-split-settled" on the document when let go. */
export declare function Splitter(o: {
  /** Where the boundary sits, as a fraction of the length the panes share. */
  value: number;
  /** Default 0.1. */
  min?: number;
  /** Default 0.9. */
  max?: number;
  /** Where Enter or a double click puts it. */
  reset: number;
  /** "vertical" between panes side by side (default), "horizontal" between one above the other. */
  orientation?: "vertical" | "horizontal";
  /** Its accessible name, e.g. "Width of the panes". */
  label: string;
  /** The id of the panes it sizes (aria-controls). */
  controls?: string;
  /** The pixels the fractions share; the parent's length less one gap where not given. */
  span?: () => number;
  /** Extra classes the page places it by, e.g. "cols", "rows", "first". */
  class?: string;
  /** Called with the new fraction, held between min and max. */
  onchange: (value: number) => void;
}): { el: HTMLElement; /** New props from the page. */ set: (p: { value?: number; min?: number; max?: number; reset?: number }) => void; readonly value: number };
export declare namespace Splitter {
  /** Two tracks sharing a length by a fraction, as CSS grid tracks. */
  function tracks(first: number): string;
  /** A fraction held between its limits; the middle where they cross. */
  function clamp(value: number, min: number, max: number): number;
  /** "ls-split-settled". */
  const SETTLED: string;
}
/** A hosted session's status in full, for its Details: the context window and what fills it, cost and lines, each usage window with when it resets, the spend limit, specialists linked to what they say, and the command that changes what the terminal shows. Uses Meter and Command. */
export declare function StatusBlock(o: {
  /** What the status line last handed over (see SessionStatus under StatusStrip). */
  status: SessionStatus;
  /** The time to measure ages and resets against, in ms. Default Date.now(). */
  now?: number;
}): { el: HTMLElement; set: (p: { status?: SessionStatus; now?: number }) => void };
/** A hosted session's status as the daemon serves it on the session row (main's lib/status.ts). Every figure is Claude's; one it did not send is null, never zero. */
export interface SessionStatus {
  version: number;
  /** When the status line last handed it over (ISO time). */
  at: string;
  /** How often Claude was asked to redraw, in seconds; null when it redraws only on its own events. */
  refreshSeconds: number | null;
  /** What the terminal itself shows. */
  terminal: "off" | "minimal" | "full";
  /** Whether the person's own status line is run for them. */
  wrapping: boolean;
  model: string | null;
  context: { usedPercent: number | null; inputTokens: number | null; windowSize: number | null; uncachedTokens: number | null; cacheReadTokens: number | null; cacheWriteTokens: number | null; outputTokens: number | null } | null;
  cost: { usd: number | null; durationMs: number | null; linesAdded: number | null; linesRemoved: number | null } | null;
  limits: { fiveHour: { usedPercent: number; resetsAt: string | null } | null; sevenDay: { usedPercent: number; resetsAt: string | null } | null; spend: { usedPercent: number | null; resetsAt: string | null; usedUsd: number | null; limitUsd: number | null; period: string | null } | null } | null;
  project: string | null;
  /** Specialist ids, e.g. "role.implementer". */
  specialists: string[] | null;
}
/** A hosted session's status as one row on the foot of its terminal: model, context, cost, the five-hour window (or the spend limit), specialists and how old the figures are. Says politely when the context crosses 70% or 90%. Put it in a `.foot` div at the end of the Terminal. */
export declare function StatusStrip(o: {
  /** What the status line last handed over. */
  status: SessionStatus;
  /** The time to measure ages and resets against, in ms. Default Date.now(). */
  now?: number;
}): { el: HTMLElement; /** New props; announces a threshold crossing once. */ set: (p: { status?: SessionStatus; now?: number }) => void };
/** One change of state, as 'task show' gives it. */
export interface TaskEvent { state: string; by: string; at: string; reason?: string | null }
/** What 'task show --json' says (main's tasks.ts TaskShowRead). */
export interface TaskShowRead {
  project: string | null; revision: string | null;
  task: { id: string; title: string; state: string; kind?: string; by?: string; declared?: string; note?: string | null; detail?: string; history?: TaskEvent[] };
  /** The idea it was accepted from, worked out from the id they share. */
  idea?: { plan: string; accepted: string; by: string } | null;
}
/** One task in full, as the dialog draws it (main's tasks.ts TaskShown). */
export interface TaskShown { id: string; title: string; state: string; declaredBy?: string; declared?: string; note?: string | null; project: string | null; detail: string; history: TaskEvent[]; idea?: TaskShowRead["idea"] }
/** One task in full, over its board: state, id and list as chips; the idea it came from; Note; Detail as Markdown; History, each change with who said so and when. Read with 'task show', and says the line ("$ loadout task show t-14 --project starstats"). Opens as a Dialog at once. */
export declare function TaskDetail(o: {
  /** The task as the card has it, shown until the full one is read. project: a slug, null for the workspace-wide list, or left out when unknown. */
  task: { id: string; title: string; state: string; project?: string | null };
  onclose?: () => void;
  /** Reads it (main: readJson('task show', words)). A rejection is shown as the problem; one named "SignedOut" goes to onsignedout. */
  read?: (path: "task show", words: string[]) => TaskShowRead | Promise<TaskShowRead>;
  /** Already read: drawn at once, without calling read. */
  shown?: TaskShown | null;
  problem?: string | null;
  onsignedout?: () => void;
  /** "Open the idea" was followed (its href is "#ideas/<project or ~>/<id>"); the dialog closes. */
  onidea?: (href: string) => void;
  /** Passed to Dialog: where it opens, whether it is held inside host, and the page that steps back. */
  host?: HTMLElement; contained?: boolean; behind?: HTMLElement;
}): { readonly el: HTMLElement | null; open: () => HTMLElement; close: () => void; set: (p: { shown?: TaskShown | null; read?: TaskShowRead; problem?: string | null }) => void };
/** One event kind, as 'trigger events --json' lists it. */
export interface TriggerEventKind { kind: string; fields: string[]; github: boolean; ending: boolean }
/** One rule, as 'trigger list --json' carries it under "rule". Spans are .NET TimeSpans ("1.00:00:00"). */
export interface TriggerRuleRead {
  id: string; on: string; projects: string[]; every?: string | null; at?: string | null; where: Record<string, string>;
  action: { kind: string; name: string; goal: string; autonomy: string };
  budgetUsd?: number | null; capUsd?: number | null; capRuns: number; window: string; debounce: string; enabled: boolean; version: number; suggestedBy?: string;
}
/** One rule and where it stands against its caps ('trigger list --json'). */
export interface TriggerRuleStanding { rule: TriggerRuleRead; problem?: string | null; runs: number; landedUsd: number; reservedUsd: number; headroomUsd?: number | null; paused: boolean; suggestionChanged: boolean }
export interface TriggerRulesOptions {
  /** The teams a rule can start, by name. */
  teams: string[];
  /** The projects a rule can watch, by slug. */
  projects: string[];
  /** Why nothing here can be pressed, when Studio is only watching. Shown in place of each command. */
  disabledReason?: string;
  /** Something went wrong that the page should say. */
  onfail?: (error: unknown) => void;
  /** main's readJson: called with 'trigger list', 'trigger events' ({kinds, builtins}), 'trigger suggested', 'trigger allowed' and 'trigger journal' ['--limit','25']. Without it, nothing is read. */
  read?: (path: string, words?: string[]) => Promise<unknown>;
  /** main's runJob: 'trigger add' (with --dry-run for Preview), 'trigger test', 'trigger resume', 'trigger remove', 'trigger accept', 'trigger allow', 'trigger disallow'. Its output is what Preview and Try it show. */
  run?: (path: string, words: string[]) => Promise<{ output?: string }>;
  /** Where the dialogs open (default document.body), and whether they stay inside it. */
  host?: HTMLElement;
  contained?: boolean;
}
/** Trigger rules: each rule against its caps, the form that writes them, teams' suggestions, who is trusted, and what the rules decided. */
export declare function TriggerRules(o: TriggerRulesOptions): {
  el: HTMLElement;
  /** Reads everything again. */
  load: () => Promise<void>;
  /** Opens the form for a new rule, or for the rule with this id. */
  add: () => void;
  change: (id: string) => void;
  set: (o: Partial<Pick<TriggerRulesOptions, "teams" | "projects" | "disabledReason" | "onfail">>) => void;
};
/* end stage 14 */

/* ---------- Commands as controls (stage 13) ---------- */

/** One input of a control, in plain words. `name` is the argument (no dashes) or the option (with them) it fills. Fields marked `more` go under More options. */
export interface ControlField { name: string; label: string; kind?: "text" | "number" | "flag" | "choice" | "lines"; /** For a choice: values that stand for a flag instead, as { "@global": "--to-global" }. */ flags?: Record<string, string>; /** An option given bare when left empty, as doctor --bundle. */ always?: boolean; options?: (string | [string, string])[]; value?: string | boolean; required?: boolean; hint?: string; placeholder?: string; unset?: string; more?: boolean; fixed?: boolean; wide?: boolean; joined?: boolean }
/** The exact line a set of fields makes: `$ loadout <path> <arguments> <options> <extra>`. */
export declare function controlLine(path: string, fields: ControlField[], values: Record<string, string | boolean>, extra?: string[]): string;
/** One thing a command does, in place: the fields people use, More options for the rest, the line it will run, Preview when it can dry-run, then the action. A session command opens a hosted terminal instead. `onRun` may return what it said, or a Node to show under the form. */
export declare function ActionForm(o: { path: string; title?: string; heading?: string; why?: string; fields?: ControlField[]; effect?: "reads" | "changes" | "session"; dryRun?: boolean; changesOnlyWith?: string[]; /** Options the action adds itself, for a command that only acts with them (--apply, --fix, --yes): Preview runs without them. */ applyWith?: string[]; /** What Preview adds instead, when the command previews with its own options (migrate: --dry-run; doctor: --fix --dry-run). */ previewWith?: string[]; extra?: string[]; label?: string; previewLabel?: string; danger?: boolean; ask?: string; yes?: string; said?: string; note?: string; compact?: boolean; disabledReason?: string; onRun?: (values: Record<string, string | boolean>, info: { preview: boolean; line: string }) => string | Node | void }): { el: HTMLElement; values: Record<string, string | boolean>; line: () => string; set: (name: string, value: string | boolean) => void; result: HTMLElement };
export interface ControlAction { label: string; cmd?: string; /** Opens this short form under the row instead of acting at once, for an action that needs a word or two (a replacement, a reason, where to). */ form?: Parameters<typeof ActionForm>[0]; glyph?: string; danger?: boolean; ask?: string; askTitle?: string; yes?: string; said?: string | ((item: ListItem) => string); disabledReason?: string; removes?: boolean; onPress?: (item?: ListItem) => string | Node | void }
/** Things done with one press, each a HaloButton with its line under it. Anything that can't be undone asks first, by name. */
export declare function ActionRow(o: { title?: string; why?: string; actions: ControlAction[] }): HTMLElement;
/** A value a command keeps: what it is now, a control to change it (text, number, choice or switch), Set it and Clear it, and the line that does it. */
export declare function SettingRow(o: { label: string; sub?: string; value?: string | null; kind?: "text" | "number" | "choice" | "switch"; options?: (string | [string, string])[]; placeholder?: string; unset?: string; valueWord?: string; choose?: string; show?: (v: string) => string; setCmd: (v: string) => string; clearCmd?: string; setLabel?: string; clearLabel?: string; setSaid?: string; clearSaid?: string; onSet?: (v: string) => string | void; onClear?: () => string | void }): HTMLElement;
export interface ListItem { id?: string; name: string; sub?: string; word?: string; facts?: string[]; actions?: ControlAction[]; detail?: () => Node; detailLabel?: string }
/** The things a command keeps, each with what can be done to it, and Add. A row's commands show under it for whichever button is pointed at or focused. */
/** How command lines show: "always", "hover" (the default: a line shows while its button, form or block is pointed at or focused, keeping its place) or "never". TerminalOnly blocks, anything inside [data-commands="always"] and disabled reasons always show. Studio keeps it as studio-commands. */
export declare const COMMANDS: readonly ["always", "hover", "never"];
/** Sets data-commands on <html> (or el); anything else falls back to "hover". */
export declare function setCommands(value: "always" | "hover" | "never", el?: HTMLElement): void;
/** The setting in force: "always", "hover" or "never". */
export declare function commandsShown(): "always" | "hover" | "never";
export declare function ItemList(o: { title?: string; label?: string; heading?: string; why?: string; items: ListItem[]; one?: string; many?: string; empty?: string; readCmd?: string; asOf?: string; add?: { label: string; form: Parameters<typeof ActionForm>[0]; toItem?: (values: Record<string, string | boolean>) => ListItem | null; said?: (item: ListItem) => string } }): { el: HTMLElement; items: () => ListItem[]; set: (items: ListItem[]) => void };
/** What a reading command says, drawn as a view (figures, a table, a document, findings or a list), from its --json. Small settings beside it; Read it again; the line it reads with. */
export declare function Readout(o: { path: string; title?: string; heading?: string; why?: string; params?: ControlField[]; extra?: string[]; view?: "figures" | "table" | "document" | "findings" | "list"; columns?: { key: string; label: string; num?: boolean; format?: (v: unknown, row: Record<string, unknown>) => string | Node }[]; data?: unknown; load?: (values: Record<string, string | boolean>) => unknown; render?: (data: unknown, values: Record<string, string | boolean>) => Node; empty?: string; waiting?: string; lazy?: boolean; readLabel?: string }): { el: HTMLElement; read: () => void; values: Record<string, string | boolean> };
/* end stage 13 */
