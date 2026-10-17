// Sets an element's style properties through the style object, the one way
// of styling from script that Studio's content security policy allows: no
// style attribute is ever written, parsed or set.

export type CssProps = Record<string, string | number | null | undefined>;

function apply(node: HTMLElement | SVGElement, next: CssProps, previous: CssProps): void {
  for (const name of Object.keys(previous)) {
    if (!(name in next)) {
      node.style.removeProperty(name);
    }
  }

  for (const [name, value] of Object.entries(next)) {
    if (value === null || value === undefined || value === '') {
      node.style.removeProperty(name);
    } else {
      node.style.setProperty(name, String(value));
    }
  }
}

/** Svelte action: use:css={{ stroke: 'var(--accent)', 'stroke-width': 2 }}. */
export function css(node: HTMLElement | SVGElement, props: CssProps) {
  let current = props ?? {};
  apply(node, current, {});

  return {
    update(next: CssProps) {
      apply(node, next ?? {}, current);
      current = next ?? {};
    },
  };
}

/** A glow in a colour, as a filter: on in Dark, nothing in the other themes. */
export function glowOf(colour: string, px: number): string {
  return `drop-shadow(0 0 ${px}px color-mix(in srgb, ${colour} calc(var(--glow-on) * 100%), transparent))`;
}

/** An arc of a circle of radius r drawn as a dash from `from` degrees for `length` degrees. */
export function arc(r: number, from: number, length: number, colour: string, width: number, centre = 270): CssProps {
  const circumference = 2 * Math.PI * r;

  return {
    stroke: colour,
    'stroke-width': width,
    'stroke-dasharray': `${((circumference * length) / 360).toFixed(1)} ${circumference.toFixed(1)}`,
    transform: `rotate(${(from - 90).toFixed(1)}deg)`,
    'transform-origin': `${centre}px ${centre}px`,
  };
}

/** A point on a circle, clockwise from the top. */
export function polar(centre: number, r: number, degrees: number): { x: number; y: number } {
  const t = (degrees * Math.PI) / 180;

  return { x: centre + r * Math.sin(t), y: centre - r * Math.cos(t) };
}
