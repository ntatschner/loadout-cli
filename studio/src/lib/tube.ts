// The Map's geometry: every piece of work is a line in its identity colour,
// the stations are its stages, and Needs you is the interchange. The numbers
// are the boards': a 1280px drawing that scrolls inside its own box.
import type { Stage } from './signals';

export interface TubeLine {
  id: string;
  code: string;
  title: string;
  kind?: string;
  where?: string;
  stage: Stage;
  waiting?: boolean;
  blocked?: string;
  failed?: string;
  held?: boolean;
  quiet?: number;
  facts?: string;
  note?: string;
  /** Checks drawn as stops after the interchange: [name, "met" | "unmet" | "pending"]. */
  checks?: [string, string][];
  /** A team run's workers, drawn as branches: [name, state]. */
  workers?: [string, string][];
  /** The last events, newest first, drawn as small ticks before their station (five at most). */
  events?: TubeEvent[];
}

export interface TubeEvent {
  stage: number;
  kind: 'asked' | 'answered' | 'round' | 'failed' | 'started' | 'ended' | 'blocked' | 'report';
  t?: string;
  text?: string;
}

export const STATION_X = [360, 540, 720, 900, 1080];
export const X0 = 326;
export const X1 = 1180;
export const LANE = 92;
export const TOP_Y = 64;

const PROFILE: Record<string, number[]> = {
  '1': [0, -18, -18, 0, 0],
  '2': [12, 12, -12, 0, 12],
  '3': [-14, 0, 16, 0, 0],
  '4': [0, 14, 0, 0, -14],
  '5': [16, 0, 0, 0, 16],
  '6': [-16, -16, 0, 0, -16],
  '7': [0, 16, 0, 0, 0],
  '8': [10, -10, 10, 0, -10],
};

export const EVENT_GLYPH: Record<string, string> = { asked: '?', answered: '✓', round: '↻', failed: '✕', started: '▶', ended: '■', blocked: '‖', report: '↩' };

export const STAGE_INK = ['var(--rest)', 'var(--shaping)', 'var(--moving)', 'var(--needs)', 'var(--good)'];

export type Point = [number, number];

/** The points a line passes through at height y, kinked by its code so lines are told apart by shape too. */
export function route(code: string, y: number): Point[] {
  const off = PROFILE[code] ?? [0, 0, 0, 0, 0];
  const points: Point[] = [[X0, y + off[0]]];

  for (let k = 0; k < 5; k++) {
    points.push([STATION_X[k], y + off[k]]);

    if (k < 4 && off[k + 1] !== off[k]) {
      const mid = (STATION_X[k] + STATION_X[k + 1]) / 2;
      const half = Math.abs(off[k + 1] - off[k]) / 2;
      points.push([mid - half, y + off[k]]);
      points.push([mid + half, y + off[k + 1]]);
    }
  }

  points.push([X1, y + off[4]]);

  return points;
}

export function pathOf(points: Point[]): string {
  return points.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(' ');
}

/** The length along the line up to x. */
export function lengthTo(points: Point[], x: number): number {
  let length = 0;

  for (let i = 1; i < points.length; i++) {
    const [ax, ay] = points[i - 1];
    const [bx, by] = points[i];
    const segment = Math.hypot(bx - ax, by - ay);

    if (bx >= x) {
      const f = bx === ax ? 1 : (x - ax) / (bx - ax);

      return length + segment * Math.max(0, Math.min(1, f));
    }

    length += segment;
  }

  return length;
}

/** The line's height at x. */
export function yAt(points: Point[], x: number): number {
  for (let i = 1; i < points.length; i++) {
    const [ax, ay] = points[i - 1];
    const [bx, by] = points[i];

    if (bx >= x) {
      const f = bx === ax ? 1 : (x - ax) / (bx - ax);

      return ay + (by - ay) * f;
    }
  }

  return points[points.length - 1][1];
}

/** The station a line is at: waiting is always the interchange. */
export function stageOf(line: Pick<TubeLine, 'waiting' | 'stage'>): Stage {
  return line.waiting ? 3 : ((line.stage | 0) as Stage);
}

/** A line's identity colour, from its code. */
export function lineColour(code: string): string {
  return `var(--line-${(((parseInt(code, 10) || 7) - 1) % 8) + 1})`;
}
