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
