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
