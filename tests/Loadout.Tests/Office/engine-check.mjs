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
  cut("  function tileDesks(room, name) {", "  function tileWalk(room, person, toX, toY) {"),
  cut("  function tileWalk(room, person, toX, toY) {", "  // Take in a fresh reading of the run"),
  cut("  /*\n    People in the way, as extra cost", "  // The next of somebody's own choices"),
  cut("  var CITY_MATERIALS = {", "  function cityFor() {"),
  cut("  function tileFloorOf(scene, x, y) {", "  // How thick a wall is"),
  cut("  var TILE_SOLID = ", "\n"),
  cut("  var TILE_GLASS = ", "\n"),
  cut("  function tileDepth(scene, person, pose) {", "  function tilePose(person) {"),
  cut("  function tilePose(person) {", "  // part \"upper\" draws only"),
  cut("  function sceneTurns(scene) {", "  /*\n    A floor turned a quarter turn"),
  cut("  function tileProp(room, ctx, prop) {", "  // What each of the built-in kit's shapes is"),
  cut("  function towerNoise(n) {", "  function towerSkyDraw("),
  cut("  function towerFacadeModule(facade, level, side, i, bays) {", "  /*\n    Which module a bay of the lobby"),
  cut("  function towerLobbyModule(facade, side, i, bays, front) {", "  /*\n    A picture halved"),
  cut("  function towerSides() {", "  /*\n    Which way a side of the tower faces"),
  cut("  function towerFacing(side) {", "  function towerDraw() {"),
  cut("  function towerNextFloor(occupied, from, by) {", "  // The level the floor view is showing"),
  cut("  function towerStopButtons(items, can) {", "  function towerServerPopup() {"),
].join("\n");

// The page's own switch for reduced motion: off, so people walk. A picture is
// anything with a size here: what tileProp draws is recorded, not shown.
const engine = new Function("function tilesStill() { return false; }\nfunction tilePicture() { return { width: 512, height: 512 }; }\n"
  + "function towerWidth() { return 1280; } function towerDepth() { return 768; }\n"
  + "var towerWaitingAt = 1; var planned = []; function plan(change) { planned.push(change); return Promise.resolve(); }\n" + code
  + "; return { tileBlocked, tilePath, tileBeside, turnScene, tileSeat, tileWalk, tileStep, cityMake,"
  + " tileCastPlace, tileSheet, tileSideDesk, tileAtDesk, tileExpression, tileFace, TILE_MOOD_EVERY, TILE_MOOD_FOR, TILE_RISE,"
  + " sceneTurns, tileProp, tilePose, tileAnimation, towerFacadeModule, towerLobbyModule, towerStopButtons, towerSides, towerNextFloor, tileDepth, tileFloorOf, tileWalls, tileDesks, towerFacing,"
  + " planned: () => planned, waitingAt: () => towerWaitingAt };")();

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

// ---- props that turn with the floor ----

// A prop's front turns with the floor, as seats do; four turns bring it back;
// it's drawn from the side its front faces; and a floor turns only once every
// picture on it has all four sides.
{
  const sides = { n: [0, 0, 64, 48], e: [64, 0, 32, 48], s: [96, 0, 64, 48], w: [160, 0, 32, 48] };
  const scene = {
    width: 6, height: 4, tile: 32, floor: [], walls: null, desks: [], door: { x: 0, y: 3, facing: "n" },
    props: [{ id: "d", piece: "desk.png", source: sides.s, sides, x: 1, y: 1, w: 2, h: 1 }],
  };
  const drawnFrom = (one) => {
    const calls = [];
    engine.tileProp({ scene: { tile: 32 }, set: "x", colours: {} }, { drawImage: (...a) => calls.push(a.slice(1, 5)) }, one);
    return JSON.stringify(calls[0]);
  };
  const seatTurn = { s: "w", w: "n", n: "e", e: "s" };
  let facing = "s";

  for (let k = 1; k <= 4; k += 1) {
    const turned = engine.turnScene(scene, k).props[0];

    facing = seatTurn[facing];

    if ((turned.facing || "s") !== facing) { fault("a prop's front doesn't turn the way seats do", `turn ${k}: ${turned.facing}, seats ${facing}`); }
    if (drawnFrom(turned) !== JSON.stringify(sides[turned.facing || "s"])) { fault("a turned prop isn't drawn from the side its front faces", `turn ${k}`); }
  }

  if ((engine.turnScene(scene, 4).props[0].facing || "s") !== "s") { fault("four turns don't bring a prop back to facing the viewer", "four turns"); }
  if (!engine.sceneTurns(scene)) { fault("a floor whose pictures all have four sides won't turn", "four sides"); }

  const short = Object.assign({}, scene, { props: [Object.assign({}, scene.props[0], { sides: { s: sides.s, n: sides.n } })] });

  if (engine.sceneTurns(short)) { fault("a floor turns with a picture that has only some of its sides", "two sides"); }
}

// ---- sitting in seats that are for sitting in ----

// Somebody with nothing to do, in a seat that says it's for sitting - a sofa
// in the lobby - sits; in one that doesn't, they stand. Doing something, they
// do it; out of the seat or on their way, they don't sit; and on an errand they
// sit only once they've got to a seat and are staying.
{
  const scene = {
    width: 6, height: 4, tile: 32, floor: [], walls: null, props: [], spots: {}, door: { x: 0, y: 3, facing: "n" },
    desks: [{ x: 2, y: 1, facing: "s", sit: true }, { x: 4, y: 1, facing: "s" }],
  };
  const room = {
    scene, seats: {},
    people: { a: { x: 2, y: 1, path: [], intent: { pose: "idle" } }, b: { x: 4, y: 1, path: [], intent: { pose: "idle" } } },
  };
  const a = room.people.a;
  const b = room.people.b;
  const checks = [];

  engine.tileSeat(room, "a");
  engine.tileSeat(room, "b");
  checks.push(["idle in a sofa seat", engine.tilePose(a), "sit"]);
  checks.push(["idle at a seat that isn't for sitting", engine.tilePose(b), "idle"]);
  a.intent = { pose: "type" };
  checks.push(["typing in a sofa seat", engine.tilePose(a), "type"]);
  a.intent = { pose: "idle" };
  a.x = 3;
  checks.push(["idle beside the seat", engine.tilePose(a), "idle"]);
  a.x = 2;
  a.path = [{ x: 3, y: 1 }];
  checks.push(["walking from the seat", engine.tilePose(a), "walk"]);
  a.path = [];
  b.errand = { stage: "going", sit: true };
  checks.push(["arriving at a seat on an errand", engine.tilePose(b), "idle"]);
  b.errand.stage = "staying";
  checks.push(["staying in a seat on an errand", engine.tilePose(b), "sit"]);
  b.errand.sit = false;
  checks.push(["staying where people stand", engine.tilePose(b), "idle"]);

  checks.filter(([, got, want]) => got !== want).forEach(([what, got, want]) => fault("a person's pose in a seat is wrong", `${what}: ${got}, not ${want}`));

  // What they're drawn with: the sofa pose in a seat for sitting, with no
  // sit-down on to an office chair first; the sit pose and its sit-down at a
  // seat that isn't; and a sheet without a sofa pose sits them as before.
  const pose = (name) => ({ frames: [0], name });
  const sheet = { animations: {} };

  ["idle", "walk", "sit", "type", "sofa", "sitdown"].forEach((kind) => ["n", "e", "s", "w"].forEach((f) => { sheet.animations[`${kind}_${f}`] = pose(`${kind}_${f}`); }));

  const plain = { animations: Object.fromEntries(Object.entries(sheet.animations).filter(([key]) => !key.startsWith("sofa_"))) };
  const drawn = (one, who) => {
    const chosen = engine.tileAnimation(one, who, engine.tilePose(who));

    return `${chosen.animation.name}${chosen.sitDown ? " after " + chosen.sitDown.name : ""}`;
  };

  a.errand = null;
  b.errand = null;
  a.intent = { pose: "idle" };
  b.intent = { pose: "sit" };
  a.facing = "e";
  b.facing = "s";

  const drawing = [
    ["idle in a sofa seat", drawn(sheet, a), "sofa_e"],
    ["sitting at a seat that isn't for sitting", drawn(sheet, b), "sit_s after sitdown_s"],
    ["idle in a sofa seat, no sofa pose", drawn(plain, a), "sit_e after sitdown_e"],
  ];

  drawing.filter(([, got, want]) => got !== want).forEach(([what, got, want]) => fault("a person in a seat is drawn with the wrong pose", `${what}: ${got}, not ${want}`));
}

// ---- the tower's facade ----

// A storey wide enough for ends and a middle has the corner at each end, the
// right one mirrored, and the kit's bays between, more than one of them along
// a storey; the same storey is drawn the same every time; a storey of two bays,
// or a kit without a corner, is bays all along.
{
  const [a, b, c, corner] = ["a", "b", "c", "corner"].map((name) => ({ name }));
  const facade = { bays: [a, b, c], corner };
  const storey = (one, level, side, bays) => Array.from({ length: bays }, (_, i) => engine.towerFacadeModule(one, level, side, i, bays));
  const row = storey(facade, 4, 1, 8);
  const middle = row.slice(1, -1);

  if (row[0].module !== corner || row[0].mirror) { fault("a storey doesn't start with the kit's corner", JSON.stringify(row[0])); }
  if (row[7].module !== corner || !row[7].mirror) { fault("a storey doesn't end with the kit's corner mirrored", JSON.stringify(row[7])); }
  if (middle.some((one) => one.module === corner || one.mirror)) { fault("a corner or a mirrored module in the middle of a storey", "middle"); }
  if (new Set(middle.map((one) => one.module.name)).size < 2) { fault("a storey's bays are all one module", middle.map((one) => one.module.name).join(" ")); }
  if (JSON.stringify(storey(facade, 4, 1, 8)) !== JSON.stringify(row)) { fault("a storey is drawn differently the second time", "same storey"); }
  if (storey(facade, 4, 1, 2).some((one) => one.module === corner)) { fault("a storey of two bays is given corners", "two bays"); }
  if (storey({ bays: [a, b, c] }, 4, 1, 8).some((one) => !facade.bays.includes(one.module))) { fault("a kit without a corner gets something other than its bays", "no corner"); }
}

// The lobby: an entrance twice a bay's width spans the two middle bays of the
// front, the second skipped, so nothing is drawn under it twice; one a bay
// wide takes the middle bay alone; any other side has none.
{
  const bay = { name: "bay", source: [0, 0, 32, 96] };
  const lobby = [{ name: "l1", source: [0, 0, 32, 192] }, { name: "l2", source: [32, 0, 32, 192] }];
  const wide = { name: "door", source: [0, 0, 64, 192] };
  const narrow = { name: "door", source: [0, 0, 32, 192] };
  const front = (entrance, isFront, bays) => Array.from({ length: bays }, (_, i) =>
    engine.towerLobbyModule({ bays: [bay], lobby, entrance }, 0, i, bays, isFront));
  const describe = (row) => row.map((one) => (one.skip ? "-" : one.module.name === "door" ? `door${one.span}` : "l")).join(" ");
  const cases = [
    [front(wide, true, 9), "l l l door2 - l l l l"],
    [front(narrow, true, 9), "l l l l door1 l l l l"],
    [front(wide, false, 9), "l l l l l l l l l"],
  ];

  cases.filter(([row, want]) => describe(row) !== want).forEach(([row, want]) => fault("the lobby's entrance is in the wrong place", `${describe(row)}, not ${want}`));
}

// The tower has one way in, on one wall, however it is turned: a door on
// both long walls showed a front door from behind.
{
  const fronts = engine.towerSides().filter((side) => side.front);

  if (fronts.length !== 1 || !fronts[0].long) { fault("the tower does not have exactly one front, on a long wall", `${fronts.length} fronts`); }
}

// The floor view's arrows step to the nearest floor with a team on it, over
// empty floors, either way, from a floor or from the lobby, roof or a
// basement, and go nowhere past the last.
{
  const occupied = [{ number: 7, run: "c" }, { number: 2, run: "a" }, { number: 4, run: "b" }];
  const step = (from, by) => (engine.towerNextFloor(occupied, from, by) || { run: "none" }).run;
  const cases = [
    [2, 1, "b"], [4, 1, "c"], [7, 1, "none"], [4, -1, "a"], [2, -1, "none"],
    [0, 1, "a"], [-2, 1, "a"], [11, -1, "c"], [5, -1, "b"], [5, 1, "c"],
  ];

  cases.filter(([from, by, want]) => step(from, by) !== want)
    .forEach(([from, by, want]) => fault("the floor arrows step to the wrong floor", `from ${from} by ${by}: ${step(from, by)}, not ${want}`));

  if (engine.towerNextFloor(undefined, 3, 1) !== null) { fault("the floor arrows step somewhere with no floors in use", "none"); }
}

// Sitting with their back to the viewer, somebody is hidden by the back of
// the chair or sofa they are in, and still drawn over the table they face;
// facing the viewer or side-on they are in front of it; standing on or
// walking over a seat they are in front of it whichever way they face.
{
  const tile = 32;
  const table = { kind: "meeting-table", x: 4, y: 3, w: 3, h: 2 };
  const chair = { kind: "chair", x: 5, y: 5, w: 1, h: 1, blocks: false };
  const scene = { tile, props: [table, chair] };
  const front = (prop) => (prop.y + prop.h) * tile;
  const at = (facing, pose) => engine.tileDepth(scene, { x: 5, y: 5, facing }, pose);
  const cases = [
    ["n", "sit", "behind"], ["n", "type", "behind"], ["n", "slump", "behind"],
    ["s", "sit", "in front"], ["e", "sit", "in front"], ["w", "sit", "in front"],
    ["n", "idle", "in front"], ["n", "walk", "in front"],
  ];

  cases.forEach(([facing, pose, want]) => {
    const y = at(facing, pose);
    const got = y < front(chair) ? "behind" : "in front";

    if (got !== want) { fault("somebody in a seat is drawn on the wrong side of it", `facing ${facing}, ${pose}: ${got}, not ${want}`); }
    if (y <= front(table)) { fault("somebody in a seat is drawn under the table they face", `facing ${facing}, ${pose}`); }
  });

  // Nobody in a seat: where their feet are, whichever way they face.
  const free = engine.tileDepth(scene, { x: 1, y: 1, facing: "n" }, "sit");

  if (free !== (1 + 0.85) * tile) { fault("somebody in no seat is moved in the drawing order", `${free}`); }
}

// Each cell is floored as the smallest room over it says, the level's floor
// outside every room, carpet where nothing says.
{
  const scene = {
    ground: "stone",
    areas: [
      { kind: "corridor", x: 0, y: 4, w: 10, h: 2, floor: "walkway" },
      { kind: "status-board", x: 3, y: 4, w: 2, h: 1 },
      { kind: "kitchen", x: 0, y: 0, w: 4, h: 4, floor: "tile" },
      { kind: "open-plan", x: 0, y: 6, w: 10, h: 6, floor: "carpet" },
      { kind: "lounge", x: 6, y: 8, w: 3, h: 3, floor: "wood" },
    ],
  };
  const cases = [[1, 1, "tile"], [3, 4, "walkway"], [7, 9, "wood"], [1, 9, "carpet"], [8, 1, "stone"]];

  cases.filter(([x, y, want]) => engine.tileFloorOf(scene, x, y) !== want)
    .forEach(([x, y, want]) => fault("a cell is floored wrongly", `${x},${y}: ${engine.tileFloorOf(scene, x, y)}, not ${want}`));

  if (engine.tileFloorOf({ areas: [] }, 0, 0) !== "carpet") { fault("a cell with nothing to say is not carpet", "none"); }
}

// Walls stand up: a run across has a face on every cell, a run down only at
// its foot; glass is told from solid by its tileset; each stands at its own
// foot, so somebody just behind it is drawn first.
{
  const tile = 32;
  const S = 12;
  const G = 20;
  const walls = [
    [-1, -1, -1, -1, -1],
    [-1, S, S, S, -1],
    [-1, -1, -1, G, -1],
    [-1, -1, -1, G, -1],
    [-1, -1, -1, -1, -1],
  ];
  const list = engine.tileWalls({ tile, width: 5, height: 5, walls, atlases: [{ tiles: 16 }, { tiles: 16 }] });
  const at = (x, y) => list.find((one) => one.x === x && one.y === y);

  if (list.length !== 5) { fault("the wrong number of wall cells", `${list.length}`); }
  if (at(1, 1).glass || !at(3, 2).glass) { fault("glass and solid walls are told apart wrongly", "kinds"); }
  if (!at(1, 1).right || at(1, 1).left || !at(3, 1).down || !at(3, 2).up || at(3, 3).down) { fault("a wall's neighbours are wrong", "neighbours"); }
  if (!(at(3, 1).ground > 1 * tile && at(3, 1).ground < 2 * tile)) { fault("a wall stands outside its own cell", `${at(3, 1).ground}`); }

  // A block of wall, two by two, is solid on top: every cell's inward corner filled.
  const block = engine.tileWalls({ tile, width: 4, height: 4, walls: [[-1, -1, -1, -1], [-1, S, S, -1], [-1, S, S, -1], [-1, -1, -1, -1]], atlases: [{ tiles: 16 }] });
  const corner = (x, y) => block.find((one) => one.x === x && one.y === y);

  if (!corner(1, 1).downRight || !corner(2, 1).downLeft || !corner(1, 2).upRight || !corner(2, 2).upLeft) { fault("a block of wall has holes in its top", "inward corners"); }
  if (corner(1, 1).upLeft || corner(2, 2).downRight || at(1, 1).downRight) { fault("a wall fills a corner with no wall in it", "outward corners"); }

  const behind = { x: 2, y: 0, facing: "s" };

  if (engine.tileDepth({ tile, props: [] }, behind, "idle") >= at(2, 1).ground) { fault("somebody behind a wall is drawn over it", "behind"); }

  const before = { x: 2, y: 2, facing: "n" };

  if (engine.tileDepth({ tile, props: [] }, before, "idle") <= at(2, 1).ground) { fault("somebody in front of a wall is drawn under it", "in front"); }
}

// On a floor teams share, everybody sits at their own team's desks: two
// teams' leads are two people, and neither takes the other's desk; and
// turning the floor turns every team's desks with it.
{
  const desk = (x, y) => ({ x, y, facing: "s" });
  const scene = {
    tile: 32, width: 12, height: 6,
    floor: Array.from({ length: 6 }, () => Array(12).fill(0)),
    walls: Array.from({ length: 6 }, () => Array(12).fill(-1)),
    props: [], areas: [], door: { x: 6, y: 5, facing: "n" },
    desks: [desk(1, 1), desk(2, 1), desk(9, 1), desk(10, 1)],
    teams: [{ run: "a", part: 0, desks: [desk(1, 1), desk(2, 1)] }, { run: "b", part: 0, desks: [desk(9, 1), desk(10, 1)] }],
  };
  const room = { scene, seats: {}, people: {} };

  // b's lead asks first, then a's: each still gets their own team's first desk.
  const bLead = engine.tileSeat(room, "b#0/lead");
  const aLead = engine.tileSeat(room, "a#0/lead");
  const aWorker = engine.tileSeat(room, "a#0/worker-1");

  room.seats = { "b#0/lead": bLead, "a#0/lead": aLead, "a#0/worker-1": aWorker };

  if (bLead.x !== 9 || aLead.x !== 1 || aWorker.x !== 2) { fault("somebody sits at another team's desk", `b lead ${bLead.x}, a lead ${aLead.x}, a worker ${aWorker.x}`); }
  if (engine.tileDesks({ scene }, "lead").length !== 4) { fault("on a floor of one run, everybody may have any desk", "plain name"); }

  const turned = engine.turnScene(scene, 1);

  if (JSON.stringify(turned.teams.map((team) => team.desks)) !== JSON.stringify([[turned.desks[0], turned.desks[1]], [turned.desks[2], turned.desks[3]]])) {
    fault("turning a shared floor leaves a team's desks where they were", "teams");
  }
}

// Each side of the tower reads its own strip of the floor's outside, by the
// way it faces: the front the south strip, then east, north and west, in
// whatever order the sides happen to be drawn.
{
  const sides = engine.towerSides();
  const facings = sides.map(engine.towerFacing);

  if (JSON.stringify(facings) !== "[0,1,2,3]") { fault("a side of the tower reads another side's outside", JSON.stringify(facings)); }
  if (engine.towerFacing(sides.find((side) => side.front)) !== 0) { fault("the front does not read the south of the floor", "front"); }
  if (JSON.stringify(sides.slice().reverse().map(engine.towerFacing)) !== "[3,2,1,0]") { fault("a side's outside depends on the order the sides are drawn in", "reversed"); }
}

// ---- stopping a schedule from the lobby ----

// Each schedule waiting has a button to stop it, asking first, and pressing it
// asks the server to remove that schedule and the lobby to look again; a task
// has none, and nor does anything on a server that can't change schedules.
{
  const waiting = [{ kind: "schedule", id: "nightly", title: "Nightly docs" }, { kind: "task", id: "fix-it" }, { kind: "schedule", id: "weekly" }];
  const buttons = engine.towerStopButtons(waiting, true);

  if (buttons.length !== 2) { fault("the lobby doesn't offer a stop for each schedule and none for a task", `${buttons.length} buttons`); }
  if (buttons.some((one) => !one.ask || !one.danger)) { fault("stopping a schedule from the lobby doesn't ask first", "no ask"); }
  if (buttons[0] && buttons[0].label !== "Stop Nightly docs") { fault("a lobby stop button doesn't say which schedule", buttons[0].label); }
  if (engine.towerStopButtons(waiting, false).length) { fault("a server that can't change schedules is offered a stop", "can't"); }

  await buttons[1].go();

  const asked = JSON.stringify(engine.planned());

  if (asked !== JSON.stringify([{ verb: "remove", name: "weekly" }])) { fault("stopping a schedule asks the server for the wrong thing", asked); }
  if (engine.waitingAt() !== 0) { fault("the lobby doesn't look again at what is waiting after a stop", String(engine.waitingAt())); }
}

// ---- tile pictures that turn with the floor ----

// A picture's tiles sit wherever its kit put them. Turned, every cell has to
// land on the tile whose corners are the built-in atlas's turned corners at
// the same cell; four turns bring every tile back; and a picture without its
// corners keeps the floor from turning.
{
  const patterns = Array.from({ length: 16 }, (_, bits) => [3, 2, 1, 0].map((b) => (bits >> b & 1 ? "u" : "l")).join(""));
  const shuffled = {};

  patterns.forEach((pattern, bits) => { shuffled[pattern] = (bits * 5 + 3) % 16; });

  const cells = [[0, 1, 2], [3, 4, 5], [6, 7, 8], [9, 10, 11], [12, 13, 14], [15, 0, 5]];
  const scene = (atlas, at) => ({
    width: 3, height: 6, tile: 32, desks: [], props: [], door: { x: 0, y: 0, facing: "s" },
    atlases: [atlas], floor: cells.map((row) => row.map(at)), walls: null,
  });
  const plain = scene({ picture: null, tiles: 16, lower: "carpet", upper: "wall" }, (bits) => bits);
  const picture = scene({ picture: "walls.png", tiles: 16, lower: "carpet", upper: "wall", corners: shuffled }, (bits) => shuffled[patterns[bits]]);
  const patternOf = Object.fromEntries(Object.entries(shuffled).map(([pattern, index]) => [index, pattern]));

  for (let k = 1; k <= 3; k += 1) {
    const want = engine.turnScene(plain, k).floor;
    const got = engine.turnScene(picture, k).floor;
    const wrong = want.flatMap((row, y) => row.map((bits, x) => (patternOf[got[y][x]] !== patterns[bits] ? `${x},${y}` : null)).filter(Boolean));

    if (wrong.length) { fault("a turned tile picture shows the wrong tile", `turn ${k}: ${wrong.slice(0, 3).join(" ")}`); }
  }

  if (JSON.stringify(engine.turnScene(picture, 4).floor) !== JSON.stringify(picture.floor)) { fault("four turns don't bring a tile picture back", "four turns"); }
  if (!engine.sceneTurns(picture)) { fault("a floor with a tile picture that gives its corners won't turn", "corners"); }

  const unmapped = Object.assign({}, picture, { atlases: [Object.assign({}, picture.atlases[0], { corners: undefined })] });

  if (engine.sceneTurns(unmapped)) { fault("a floor turns with a tile picture that doesn't say which tile has which corners", "no corners"); }

  // The dual grid: a tile at every point where four cells meet, chosen by
  // them. Turned with the floor, it has to be the dual grid of the turned
  // cells, worked out here from the cells themselves, not from the page.
  const walls = [[1, 1, 1], [1, 0, 0], [0, 0, 1], [0, 0, 0], [1, 0, 0], [0, 1, 1]];
  const turnCells = (rows) => rows[0].map((_, y) => rows.map((_, x) => rows[rows.length - 1 - x][y]));
  const dualOf = (rows) => {
    const h = rows.length;
    const w = rows[0].length;
    const at = (x, y) => rows[Math.min(h - 1, Math.max(0, y))][Math.min(w - 1, Math.max(0, x))] ? "u" : "l";

    return Array.from({ length: h + 1 }, (_, y) => Array.from({ length: w + 1 }, (_, x) =>
      shuffled[at(x - 1, y - 1) + at(x, y - 1) + at(x - 1, y) + at(x, y)]));
  };
  const dualScene = Object.assign({}, picture, { surface: dualOf(walls) });
  let cellsTurned = walls;

  for (let k = 1; k <= 4; k += 1) {
    cellsTurned = turnCells(cellsTurned);

    const got = JSON.stringify(engine.turnScene(dualScene, k).surface);
    const want = JSON.stringify(dualOf(cellsTurned));

    if (got !== want) { fault("a turned floor's dual grid isn't the dual grid of its turned cells", `turn ${k}`); }
  }
}

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
