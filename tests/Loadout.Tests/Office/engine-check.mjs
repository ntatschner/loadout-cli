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
].join("\n");

const engine = new Function(code + "; return { tileBlocked, tilePath, tileBeside, turnScene };")();

const lines = readline.createInterface({ input: fs.createReadStream(process.argv[2]) });
const faults = new Map();
let scenes = 0;
let paths = 0;

function fault(kind, where) {
  if (!faults.has(kind)) { faults.set(kind, []); }
  faults.get(kind).push(where);
}

for await (const line of lines) {
  const { name, scene: raw } = JSON.parse(line);

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

console.log(`${scenes} scenes (every scene at four turns), ${paths} paths from the door`);

if (!faults.size) {
  console.log("the page's engine reaches everything, one tile at a time, never through anything");
} else {
  for (const [kind, where] of faults) {
    console.log(`FAULT ${kind}: ${where.length} time(s); first: ${where.slice(0, 3).join(" | ")}`);
  }
}

process.exit(faults.size ? 1 : 0);
