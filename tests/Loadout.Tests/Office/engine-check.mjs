// The office page's own engine, run against scenes the planner generated.
//
// Usage: node engine-check.mjs <scenes.jsonl> <dashboard.html>
//
// The functions are cut out of the page as it ships, not copied, so what is
// tested is what runs in the browser. For each scene at all four turns: from
// the door, every desk, every named spot and the place beside each desk a
// visitor is sent to can be walked to and back; every path moves a tile at a
// time and never through a wall or anything that blocks; and nobody is ever
// sent to stand inside something. What is in the way is worked out here from
// the scene rather than taken from the page, because a checker that used the
// page's own answer would agree with any mistake in it.
//
// Then people: on a spread of those scenes, the page's own movement - where a
// person sits, planning a walk, stepping a frame at a time - is run with a
// team arriving through the door a few seconds apart, everybody going
// somewhere different, and everybody coming back. Nobody may fail to arrive,
// and no two people may share a tile for longer than squeezing past takes.
//
// And the neighbourhood round the tower, generated in the page as meshes: for
// several tile and floor sizes, no building overlaps another, a road or the
// tower's block; every one is lower than the tower; no tree stands in a
// building; every face is a flat rectangle facing the way it says; and the
// same sizes always give the same neighbourhood.
//
// Run by OfficeEngineTests. Exit 0 when all is well; 1 with every fault by
// kind, a count and the first few places, otherwise.
import fs from "node:fs";
import readline from "node:readline";

const page = fs.readFileSync(process.argv[3], "utf8");

// What is in the way, worked out here rather than taken from the page: a
// checker that used the page's own answer would agree with any mistake in it.
function obstacles(scene) {
  const grid = [];

  for (let y = 0; y < scene.height; y += 1) {
    for (let x = 0; x < scene.width; x += 1) {
      const floor = (scene.floor[y] || [])[x];
      const wall = scene.walls ? (scene.walls[y] || [])[x] : -1;

      grid.push(floor === undefined || floor < 0 || (wall !== undefined && wall >= 0));
    }
  }

  for (const prop of scene.props || []) {
    if (prop.blocks === false) { continue; }

    for (let y = prop.y; y < prop.y + (prop.h || 1); y += 1) {
      for (let x = prop.x; x < prop.x + (prop.w || 1); x += 1) {
        grid[y * scene.width + x] = true;
      }
    }
  }

  return grid;
}

function cut(start, end) {
  const from = page.indexOf(start);
  const to = page.indexOf(end, from + start.length);

  if (from < 0 || to < 0) { throw new Error("not found: " + start); }

  return page.slice(from, to);
}

const code = [
  cut("  function tileBlocked(scene) {", "  /*\n    The tiles from one place to another"),
  cut("  var TILE_STEPS =", "  /*\n    Where each person belongs."),
  cut("  function tileBeside(room, seat) {", "  // Somewhere for somebody with nothing") ,
  cut("  function turnScene(scene, k) {", "  // The tower beyond the floor's glass"),
  cut("  var TILE_PACE", "\n"),
  cut("  function tileSeat(room, name) {", "  function tileWalk(room, person, toX, toY) {"),
  cut("  function tileWalk(room, person, toX, toY) {", "  // Take in a fresh reading of the run"),
  cut("  /*\n    People in the way, as extra cost", "  // The next of somebody's own choices"),
  cut("  var CITY_MATERIALS = {", "  function cityFor() {"),
].join("\n");

// The page's own switch for reduced motion: off, so people walk.
const engine = new Function("function tilesStill() { return false; }\n" + code
  + "; return { tileBlocked, tilePath, tileBeside, turnScene, tileSeat, tileWalk, tileStep, cityMake };")();

const lines = readline.createInterface({ input: fs.createReadStream(process.argv[2]) });
const faults = new Map();
let scenes = 0;
let paths = 0;

function fault(kind, where) {
  if (!faults.has(kind)) { faults.set(kind, []); }
  faults.get(kind).push(where);
}

const chosen = [];
const walked = /^floor team (5|10|15|20|25|30) seed [0-4]$|^lobby (16|32)$|^roof (16|32)$|^basement/;

for await (const line of lines) {
  const { name, scene: raw } = JSON.parse(line);

  if (walked.test(name)) { chosen.push([name, raw]); }

  for (let k = 0; k < 4; k += 1) {
    const scene = engine.turnScene(raw, k);
    const room = { scene, blocked: engine.tileBlocked(scene), seats: {} };
    const solid = obstacles(scene);
    const w = scene.width;
    const door = scene.door;
    const where = `${name}, turn ${k}`;

    scenes += 1;

    if (solid[door.y * w + door.x]) { fault("the door is blocked", where); continue; }

    scene.desks.forEach((desk, i) => { room.seats["n" + i] = desk; });

    // What the page can reach from the door, by its own blocking.
    const reach = new Set([door.y * w + door.x]);
    const queue = [[door.x, door.y]];

    while (queue.length) {
      const [x, y] = queue.shift();

      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx;
        const ny = y + dy;
        const at = ny * w + nx;

        if (nx < 0 || ny < 0 || nx >= w || ny >= scene.height || room.blocked[at] || reach.has(at)) { continue; }

        reach.add(at);
        queue.push([nx, ny]);
      }
    }

    const targets = [];

    scene.desks.forEach((desk, i) => {
      targets.push(["desk " + i, desk]);

      const beside = engine.tileBeside(room, desk);

      if (beside) { targets.push(["beside desk " + i, beside]); }
    });

    Object.entries(scene.spots || {}).forEach(([spot, at]) => targets.push(["spot " + spot, at]));

    for (const [what, to] of targets) {
      if (to.x === door.x && to.y === door.y) { continue; }

      // Everywhere somebody is sent is somewhere they can stand.
      if (solid[to.y * w + to.x]) { fault("somebody would stand inside something", `${where}: ${what} at ${to.x},${to.y}`); }

      paths += 1;

      const path = engine.tilePath(room, door.x, door.y, to.x, to.y);

      if (!path.length) {
        fault(`no way from the door to a ${what.split(" ")[0]}${what.startsWith("beside") ? " (beside)" : ""}`, `${where}: ${what} at ${to.x},${to.y}`);
        continue;
      }

      let px = door.x;
      let py = door.y;

      for (let i = 0; i < path.length; i += 1) {
        const step = path[i];

        if (Math.abs(step.x - px) + Math.abs(step.y - py) !== 1) { fault("a path jumps", `${where}: ${what}`); break; }
        if (i < path.length - 1 && solid[step.y * w + step.x]) { fault("a path goes through something", `${where}: ${what} at step ${step.x},${step.y}`); break; }

        px = step.x;
        py = step.y;
      }

      if (px !== to.x || py !== to.y) { fault("a path ends somewhere else", `${where}: ${what}`); }

      // The way back, which is what somebody walking home from an errand uses.
      if (!room.blocked[to.y * w + to.x] && !engine.tilePath(room, to.x, to.y, door.x, door.y).length) {
        fault("no way back to the door", `${where}: ${what}`);
      }
    }
  }
}

// ---- people walking ----

const FRAME = 50;
const SHARED = 2500;
const PHASE = 90000;
let walks = 0;
let sharedMs = 0;
let longestShare = 0;

function simulate(scene, where) {
  const room = { scene, blocked: engine.tileBlocked(scene), people: {}, seats: {}, last: 0 };
  const w = scene.width;
  const door = scene.door;
  const doorCell = door.y * w + door.x;
  const count = Math.min(scene.desks.length, 14);
  const names = Array.from({ length: count }, (_, i) => "p" + String(i).padStart(2, "0"));
  const sharing = new Map();
  let now = 1000;

  room.last = now;

  function frames(until, starts) {
    const pending = starts.slice();

    for (; now < until; now += FRAME) {
      while (pending.length && pending[0].at <= now) {
        const start = pending.shift();

        engine.tileWalk(room, room.people[start.name], start.to.x, start.to.y);
        walks += 1;
      }

      engine.tileStep(room, now);

      // Who shares a tile with whom, away from the door, where everybody arrives at once.
      const cells = new Map();

      for (const name of names) {
        const one = room.people[name];
        const cell = Math.round(one.y) * w + Math.round(one.x);

        if (cell === doorCell) { continue; }
        if (!cells.has(cell)) { cells.set(cell, []); }
        cells.get(cell).push(name);
      }

      const now2 = new Set();

      for (const [, here] of cells) {
        for (let i = 0; i < here.length; i += 1) {
          for (let j = i + 1; j < here.length; j += 1) {
            const pair = here[i] + "|" + here[j];

            now2.add(pair);
            sharing.set(pair, (sharing.get(pair) || 0) + FRAME);
            sharedMs += FRAME;
            longestShare = Math.max(longestShare, sharing.get(pair));

            if (sharing.get(pair) === SHARED) {
              fault("two people shared a tile for longer than squeezing past takes", `${where}: ${pair}`);
            }
          }
        }
      }

      for (const pair of [...sharing.keys()]) {
        if (!now2.has(pair)) { sharing.delete(pair); }
      }

      if (!pending.length && names.every((name) => !room.people[name].path.length)) { break; }
    }
  }

  function arrived(goals, what) {
    for (const name of names) {
      const one = room.people[name];
      const goal = goals[name];

      if (one.path.length || Math.round(one.x) !== goal.x || Math.round(one.y) !== goal.y) {
        fault("somebody never got where they were going", `${where}: ${name} ${what}, at ${one.x.toFixed(1)},${one.y.toFixed(1)} for ${goal.x},${goal.y}`);
        return false;
      }
    }

    return true;
  }

  for (const name of names) {
    room.people[name] = { x: door.x, y: door.y, path: [], facing: "s", leaving: false, gone: false, seed: 0, rng: 1, errand: null, nextErrand: 0 };
  }

  // Arriving: through the door two seconds apart, each to their own seat.
  const seats = {};

  names.forEach((name) => { seats[name] = engine.tileSeat(room, name); });
  frames(now + PHASE, names.map((name, i) => ({ name, at: now + i * 2000, to: seats[name] })));

  if (!arrived(seats, "arriving")) { return; }

  // An errand each: every named spot, and beside every other desk, handed out
  // so that no two people are sent to the same tile, as the page's own
  // errands never are.
  const places = Object.values(scene.spots || {}).map((spot) => ({ x: spot.x, y: spot.y }));

  names.forEach((name) => {
    const beside = engine.tileBeside(room, seats[name]);

    if (beside) { places.push({ x: beside.x, y: beside.y }); }
  });

  const taken = new Set(Object.values(seats).map((seat) => seat.y * w + seat.x));
  const errands = {};
  let next = 0;

  for (const name of names) {
    for (let tries = 0; tries < places.length; tries += 1) {
      const place = places[(next + tries * 7) % places.length];
      const cell = place.y * w + place.x;

      if (!taken.has(cell) && cell !== doorCell && !room.blocked[cell]) {
        errands[name] = place;
        taken.add(cell);
        break;
      }
    }

    errands[name] = errands[name] || seats[name];
    next += 3;
  }

  frames(now + PHASE, names.map((name, i) => ({ name, at: now + i * 300, to: errands[name] })));

  if (!arrived(errands, "on an errand")) { return; }

  // And back, all at once.
  frames(now + PHASE, names.map((name) => ({ name, at: now, to: seats[name] })));
  arrived(seats, "coming back");
}

let simulated = 0;

for (const [name, raw] of chosen) {
  for (const k of [0, 1]) {
    simulate(engine.turnScene(raw, k), `${name}, turn ${k}`);
    simulated += 1;
  }
}

// ---- the neighbourhood ----

let cities = 0;
let blocks = 0;

for (const [t, cols, rows] of [[32, 24, 16], [16, 24, 16], [32, 30, 20], [32, 16, 12]]) {
  const w = cols * t;
  const d = rows * t;
  const s = t * 3;
  const where = `neighbourhood for ${t}px tiles, a ${cols}x${rows} floor`;
  const city = engine.cityMake(t, w, d, s);
  const again = engine.cityMake(t, w, d, s);
  const pave = t * 1.5;
  const tower = { x0: -pave, y0: -pave, x1: w + pave, y1: d + pave };
  const lowest = 12 * s;

  cities += 1;
  blocks += city.buildings.length;

  if (JSON.stringify(city.faces) !== JSON.stringify(again.faces)) { fault("the neighbourhood changes from one making to the next", where); }

  const overlap = (a, b) => a.x0 < b.x1 && b.x0 < a.x1 && a.y0 < b.y1 && b.y0 < a.y1;
  const roads = city.faces.filter((face) => face.m === "road").map((face) => ({
    x0: Math.min(...face.c.map((p) => p[0])), x1: Math.max(...face.c.map((p) => p[0])),
    y0: Math.min(...face.c.map((p) => p[1])), y1: Math.max(...face.c.map((p) => p[1])),
  }));

  city.buildings.forEach((one, i) => {
    if (overlap(one, tower)) { fault("a neighbour stands on the tower's block", `${where}: building ${i}`); }
    if (roads.some((road) => overlap(one, road))) { fault("a neighbour stands in a road", `${where}: building ${i}`); }
    if (city.buildings.some((other, j) => j > i && overlap(one, other))) { fault("two neighbours overlap", `${where}: building ${i}`); }
    if (one.z >= lowest) { fault("a neighbour is as tall as the tower", `${where}: building ${i}, ${one.z / s} storeys`); }
  });

  city.trees.forEach(([x, y], i) => {
    if (city.buildings.some((one) => x > one.x0 && x < one.x1 && y > one.y0 && y < one.y1)) { fault("a tree stands in a building", `${where}: tree ${i}`); }
  });

  city.faces.forEach((face, i) => {
    const [a, b, , c] = face.c;
    const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    const n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const length = Math.hypot(...n);
    const square = Math.abs(u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) < 1e-6;
    const flat = [0, 1, 2].every((k) => Math.abs(face.c[2][k] - (b[k] + c[k] - a[k])) < 1e-6);
    // Which way it faces, whichever way round its corners go.
    const facing = length > 0 && Math.abs(Math.abs((n[0] * face.n[0] + n[1] * face.n[1] + n[2] * face.n[2]) / length) - 1) < 1e-6;

    if (!square || !flat || !facing) { fault("a face is not the rectangle it says it is", `${where}: face ${i} (${face.m})`); }
  });
}

console.log(`${scenes} scenes (every scene at four turns), ${paths} paths from the door`);
console.log(`${cities} neighbourhoods made: ${blocks} buildings, none overlapping, all lower than the tower`);
console.log(`${simulated} scenes walked: ${walks} walks, two people on one tile for ${(sharedMs / 1000).toFixed(1)}s in all, at most ${(longestShare / 1000).toFixed(2)}s at a time`);

if (!faults.size) {
  console.log("the page's engine reaches everything, one tile at a time, never through anything, and people get where they are going without standing in each other");
} else {
  for (const [kind, where] of faults) {
    console.log(`FAULT ${kind}: ${where.length} time(s); first: ${where.slice(0, 3).join(" | ")}`);
  }
}

process.exit(faults.size ? 1 : 0);
