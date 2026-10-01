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
  cut("  function tilePose(person) {", "  // part \"upper\" draws only"),
].join("\n");

// The page's own switch for reduced motion: off, so people walk.
const engine = new Function("function tilesStill() { return false; }\n" + code
  + "; return { tileBlocked, tilePath, tileBeside, turnScene, tileSeat, tileWalk, tileStep, cityMake,"
  + " tileCastPlace, tileSheet, tileSideDesk, tileAtDesk, tileExpression, tileFace, TILE_MOOD_EVERY, TILE_MOOD_FOR, TILE_RISE };")();

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

// ---- who is drawn as whom ----

// A kit's cast: one sheet for the lead's role, nine for everybody else. Which
// of them draws each of a run's nodes is the server's choice (OfficeCast), so
// the name fits the face; the page has to draw exactly who it is told, keep
// them on the roof, and pick for itself only for people the server does not
// cast - somebody waiting in the lobby, the keeper.
const workers = ["w0", "w1", "w2", "w3", "w4", "w5", "w6", "w7", "w8"];
const sheets = Object.fromEntries(["lead", "porter", ...workers].map((name) => [name, { piece: name + ".png" }]));
const castScene = { sheets, cast: { "project-lead": ["lead"], keeper: ["porter"], worker: workers } };
let casts = 0;

for (const name of workers) {
  const node = { node: "implementer/1", role: "role.implementer", cast: name };
  const floor = engine.tileSheet(castScene, "implementer", { node, cast: engine.tileCastPlace({ id: "r" }, node) });
  // On the roof the node is copied under "run|node", its cast with it.
  const roofNode = Object.assign({}, node, { node: "r|implementer/1" });
  const roof = engine.tileSheet(castScene, "implementer", { node: roofNode, cast: engine.tileCastPlace({ id: "roof" }, roofNode) });

  casts += 1;

  if (floor !== sheets[name]) { fault("somebody is not drawn as the server cast them", name); }
  if (roof !== floor) { fault("somebody looks different on the roof from on their own floor", name); }
}

// Nobody the server cast: their role's list, the same pick every time, and never the wrong role's.
for (const who of ["waiting-task-12", "waiting-schedule-3", "keeper"]) {
  const node = { node: who };
  const role = who === "keeper" ? "keeper" : "task";
  const first = engine.tileSheet(castScene, role, { node, cast: engine.tileCastPlace({ id: "lobby" }, node) });
  const again = engine.tileSheet(castScene, role, { node, cast: engine.tileCastPlace({ id: "lobby" }, node) });

  if (!first) { fault("somebody the server did not cast is drawn with no sheet", who); }
  if (first !== again) { fault("somebody the server did not cast changes from one drawing to the next", who); }
  if (role === "keeper" && first !== sheets.porter) { fault("the keeper is not drawn from the keeper's list", who); }
  if (role !== "keeper" && !workers.some((name) => sheets[name] === first)) { fault("somebody waiting is not drawn from the workers", who); }
}

// A cast naming a sheet the set does not have falls back on the role's list rather than drawing nobody.
if (!engine.tileSheet(castScene, "implementer", { node: { node: "x", cast: "nobody" }, cast: 0 })) {
  fault("a cast sheet the set lacks draws nobody at all", "nobody");
}

// A set with no cast keeps drawing a role with its one sheet.
if (engine.tileSheet({ sheets, skins: { worker: "w3" } }, "implementer", { node: { node: "x" }, cast: 7 }) !== sheets.w3) {
  fault("a set's skins are not used when it has no cast", "skins only");
}

console.log(`${casts} people drawn as the server cast them, on their floor and on the roof; the uncast drawn from their role's list`);

// ---- sitting at a desk side-on ----

// Somebody sitting facing along the floor is drawn twice, the second time
// above their lap over the desk they face, so their hands are on it. That
// needs the desk to be found: at every turn of every floor walked above, a
// seat facing east or west must have its desk straight ahead, and nobody
// facing up or down the floor, or walking, may be taken for sitting side-on.
let sideOn = 0;

// Working floors only: the lobby's and the roof's seats are sofas and benches, with no desk to sit at.
for (const [name, raw] of chosen.filter(([name]) => name.startsWith("floor"))) {
  for (let k = 0; k < 4; k += 1) {
    const scene = engine.turnScene(raw, k);
    const room = { scene, people: {} };

    scene.desks.forEach((seat, i) => {
      const person = { x: seat.x, y: seat.y, facing: seat.facing, path: [], intent: { pose: "type" }, gone: false };
      const where = `${name}, turn ${k}, desk ${i} facing ${seat.facing}`;

      room.people.p = person;

      const desk = engine.tileSideDesk(room, person);

      if (seat.facing === "e" || seat.facing === "w") {
        sideOn += 1;

        if (!desk) { fault("somebody sitting side-on has no desk in front of them", where); return; }
        if (!engine.tileAtDesk(room, desk).includes(person)) { fault("a desk does not know who sits side-on at it", where); }
      } else if (desk) {
        fault("somebody facing up or down the floor is taken for sitting side-on", where);
      }

      person.path = [{ x: seat.x, y: seat.y + 1 }];

      if (engine.tileSideDesk(room, person)) { fault("somebody walking is taken for sitting at a desk", where); }
    });
  }
}

// Turned floors always have seats side-on; none would mean this checked nothing.
if (!sideOn) { fault("no seat on any turned floor faces along it, so sitting side-on went unchecked", "every floor"); }

console.log(`${sideOn} seats side-on to their desk, each finding the desk in front of it`);

// ---- the face layer ----

// Which expression: from what the node is doing first, and a passing mood only now and then.
const said = (intent, pose, extra) => engine.tileExpression(Object.assign({ intent, seed: 0.3 }, extra || {}), pose, 1234);

if (said({ lamp: "failed" }, "type") !== "frown") { fault("a failed node does not frown", "failed"); }
if (said({ lamp: "waiting" }, "idle") !== "surprised") { fault("a node waiting on you does not look up", "waiting"); }
if (said({ lamp: "quiet" }, "walk", { leaving: true }) !== "smile") { fault("somebody leaving done does not smile", "leaving"); }
if (said({ lamp: "working" }, "type") !== "focused") { fault("somebody typing does not look focused", "typing"); }
if (said({ lamp: "quiet" }, "slump") !== "tired") { fault("somebody slumped does not look tired", "slump"); }

let moodMs = 0;
const moodAt = new Set();

for (let now = 0; now < engine.TILE_MOOD_EVERY * 4; now += 100) {
  if (engine.tileExpression({ intent: { lamp: "quiet" }, seed: 0.3 }, "idle", now)) { moodMs += 100; }
}

for (const seed of [0.1, 0.35, 0.6, 0.85]) {
  for (let now = 0; now < engine.TILE_MOOD_EVERY; now += 500) {
    if (engine.tileExpression({ intent: { lamp: "quiet" }, seed }, "idle", now)) { moodAt.add(Math.floor(now / 1000)); }
  }
}

if (!moodMs || moodMs > engine.TILE_MOOD_FOR * 4 + 100) { fault("a passing mood is never there, or there too long", `${moodMs}ms in ${engine.TILE_MOOD_EVERY * 4}ms`); }
if (moodAt.size < 8) { fault("everybody's passing mood comes at the same moment", `${moodAt.size} different seconds for four people`); }

// Drawing: a made-up face, front and side, at every expression and with every trait.
const front = { eyes: [[40.5, 30], [49.5, 30]], mouth: [45, 36] };
const side = { eyes: [[50, 30]], mouth: [53, 35] };
const look = { skin: "#c08868", brow: "#20181a", lip: "#7a4a3f" };
const expressions = ["smile", "focused", "tired", "frown", "surprised", "yawn"];
let drawn = 0;

for (const traits of [[], ["beard"], ["glasses"], ["eyes_closed"], ["grin"]]) {
  const sheet = { faces: { 3: front, 4: side }, face: Object.assign({ traits }, look) };

  for (const expression of expressions.concat([null])) {
    for (const [frame, facing] of [[3, "s"], [4, "e"], [4, "w"], [9, "s"]]) {
      const pixels = [];
      const ctx = { fillStyle: "", fillRect(x, y) { pixels.push({ x, y, colour: this.fillStyle }); } };
      const where = `${expression} facing ${facing}, traits ${traits.join(",") || "none"}`;
      const place = sheet.faces[frame];

      engine.tileFace(ctx, sheet, frame, facing, expression, 100, 200);
      drawn += pixels.length;

      if ((!expression || !place) && pixels.length) { fault("a face is drawn on with nothing to draw", where); continue; }
      if (!expression || !place) { continue; }
      if (!pixels.length) { fault("an expression draws nothing", where); }

      for (const p of pixels) {
        const x = p.x - 100;
        const y = p.y - 200;
        const nearMouth = Math.abs(x - place.mouth[0]) <= 4 && y >= place.mouth[1] - 1 && y <= place.mouth[1] + 2;
        const onEyes = place.eyes.some(([ex, ey]) => Math.abs(x - ex) <= 2 && y >= ey - 1 && y <= ey);

        if (!nearMouth && !onEyes) { fault("the face layer draws away from the face", `${where}: ${x},${y}`); break; }
        if (onEyes && !nearMouth && (traits.includes("glasses") || traits.includes("eyes_closed"))) { fault("eyes behind glasses or already closed are drawn over", where); break; }
        if (p.colour === look.skin && (place.eyes.length === 1 || traits.includes("beard"))) {
          if (!onEyes) { fault("skin is painted over a side view's mouth or a beard", where); break; }
        }
      }
    }
  }
}

console.log(`face layer: ${drawn} pixels across every expression and trait, none away from the face`);

// ---- getting up before walking off ----

// Somebody in their chair, whose sheet can sit down, is given somewhere to go:
// they stay put while they get up, then walk. Somebody who can't sit down that
// way walks at once, so nothing built-in is held up.
{
  const [name, raw] = chosen.find(([one]) => one.startsWith("floor"));
  const scene = engine.turnScene(raw, 0);
  const seat = scene.desks[0];

  for (const canSit of [true, false]) {
    const room = { scene, blocked: engine.tileBlocked(scene), seats: {}, people: {} };
    const person = { x: seat.x, y: seat.y, facing: seat.facing, path: [], seated: true, canSit, gone: false, leaving: false, intent: {} };

    room.people.p = person;
    room.seats.p = seat;
    engine.tileWalk(room, person, scene.door.x, scene.door.y);

    if (!person.path.length) { fault("no way from a desk to the door to test getting up on", name); break; }

    let movedAt = -1;

    for (let now = 1000; now < 1000 + engine.TILE_RISE * 3; now += 50) {
      engine.tileStep(room, now);

      if (movedAt < 0 && (person.x !== seat.x || person.y !== seat.y)) { movedAt = now - 1000; }
    }

    const where = `${name}, ${canSit ? "a sheet that sits down" : "no sit-down"}: first moved after ${movedAt}ms`;

    if (canSit && (movedAt < 0 || movedAt < engine.TILE_RISE - 50)) { fault("somebody walks off before they've got up", where); }
    if (!canSit && (movedAt < 0 || movedAt > 200)) { fault("somebody with no sit-down is held in their chair", where); }
    if (movedAt < 0) { fault("somebody never leaves their chair", where); }
  }
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
