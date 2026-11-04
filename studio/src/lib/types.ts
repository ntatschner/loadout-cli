// Shapes shared between components.

export interface HaloButtonOptions {
  label: string;
  kind?: '' | 'needs' | 'danger';
  size?: '' | 'small' | 'big';
  solid?: boolean;
  glyph?: string;
  cmd?: string;
  disabledReason?: string;
}

export interface PickerOption {
  name: string;
  sub?: string;
  glyph?: string;
}

/** One page in the side rail, with its live count in words. */
export interface RailPage {
  key: string;
  name: string;
  fact?: string;
  need?: boolean;
  href?: string;
}

/** Other work circling the halo, which a press brings into it. */
export interface Orbiter {
  key: string;
  title: string;
  word: string;
  onpick?: () => void;
}

/**
 * The About Loadout model, as 'loadout about --json' prints it and the daemon
 * serves it at /api/v1/about/loadout. Not the page's About, which says where
 * Studio is served from.
 */
export interface AboutLoadout {
  version: string;
  devBuild: boolean;
  available: string | null;
  updateAvailable: boolean;
  features: { id: string; name: string; since: string; status: string; guide: string; installed: boolean }[];
  guides: { key: string; title: string; url: string }[];
  timeline: {
    releases: { version: string; name: string; published: string | null; notes: string; url: string; installed: boolean; kind: string }[];
    /** Why there are no releases, when there are none: Off, Offline, NotGitHub, RateLimitedNoCache or Unreachable. */
    empty: string | null;
    /** The sentence every surface shows for that reason, written by the daemon. */
    emptyText?: string;
    fetchedAt: string | null;
    stale: boolean;
  };
}
