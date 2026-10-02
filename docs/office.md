# The office

The dashboard's **Office** view draws your team runs as a building: one floor
per run, with the people on it at their desks, in meetings, at the coffee
machine or up on the roof. It is there to be glanced at. Everything it shows is
also in the List, Graph and Timeline views, which are the ones to use with a
screen reader or when you want the detail; the office is deliberately a
picture, and isn't built to be read aloud.

## Outside

You start outside, looking at the tower from above a corner. **Corner**,
**Aerial** and **From the street** change where you're standing; the turn
buttons (or Q and E) walk you round it a quarter at a time. At the first zoom
the whole building fits. Zoom in (the + button, Ctrl and the wheel, or a pinch)
and you can scroll up and down it, starting at the floors in use.

**The lobby** is the close-up: across the street from the entrance, looking in
through the glass at the receptionist behind the desk and everyone waiting on
the sofas, each with the name of what they're waiting for over them. They're the
same people the lobby shows from inside, doing the same things, so a run that
starts has its lead walk in off the street while you watch. Point at somebody to
see what they are and when it's due; click to go in. The turn buttons take you
round to the lobby's other sides.

What you can read from out here:

- **Lit windows** on a floor in use, more of them the fuller it is, with
  somebody in the first few.
- **A strip along the floor's slab** in its run's colour: working, waiting on
  you, or failed.
- **The name on the crown**, lit in the colour of whatever most needs seeing
  anywhere in the building.
- **The lobby** at street level, **the roof** on top and **two basements**
  showing faintly through the street.

Hover over a floor to see whose it is; click it to go in.

Round the tower is a neighbourhood: blocks of other buildings, parks and roads,
made in the page as 3D shapes and drawn through the same camera, so it turns
and zooms with the tower. It's the same neighbourhood for everybody and doesn't
rearrange itself, every building in it is lower than the tower, and anything
standing between you and the tower is faded so it never hides it.

The street round the tower changes with your clock: day, dusk, night and dawn,
with the lamps and headlights on after dark. Some days it rains, and in winter
it sometimes snows. The weather comes from the date, so everyone looking today
sees the same. Add `office-sky=night` or `office-weather=rain` to the page's
address to see one without waiting for it. With reduced motion on, it's all
drawn still.

## Floors

Teams share floors. A floor's south side is split into three bays, and each
running run gets as many as its people need, side by side, on the lowest floor
with that many free: a small team has neighbours, a big one takes a floor or
more. The building is never shorter than ten floors and grows past that when it
has to. A team that outgrows its bays takes the one beside it, or room on the
floor above, and gives the last back once it has shrunk and stayed small for a
few minutes, so a team hovering at the edge doesn't flicker. Nobody else's bays
move when one team comes, grows or goes. When a run finishes, its bays go dark
and stay that way for an hour, so you can still go and look, then they're freed.

Each team's bays are laid out from the run's own identifier, so the same run
gets the same desks every time, whoever its neighbours are, and two runs get
different ones; the shared rooms are laid out for everybody on the floor. The core is in
the middle of the north wall, the same place on every level so the lift lines
up through the tower, with the toilets, a cupboard and the stairs. Either side
of it, along the north wall, are the floor's rooms: a kitchen and meeting rooms
first, then a lounge for a bigger team, and as room allows a library, a print
corner, phone booths (one for every eight people), a wellness room and a
training room for the biggest teams, a storage cupboard. Which goes where comes from the
rules, each room's zone and what it should be near or away from, and each has
a door onto the corridor that runs the width of the floor. Band left over
becomes an open nook with a sofa.

Each team works along the south windows, in an area of its bays as big as it needs:
the lead's office in its window corner, the lead facing the door, and desks in
pods facing each other across the monitors. Some floors leave it open, its
carpet against the bare floor round it; others give it walls and a door, as a
team room. What no team uses is vacant, bare concrete with nobody on it, for
the next team to take. Rooms are furnished from the pieces that say they
suit them: the counter in the kitchen with the coffee machine beside it, a
coffee table in front of the sofa, a visitor's chair across the lead's desk,
the copier and the water cooler against the corridor wall.

Going in shows the whole floor, everybody on it. The header names the floor's
teams and each team's area has its name over it. Clicking a team's own office
or desks is about that run; clicking a room the floor shares, the meeting room
or the status board, shows each team's part of it under its name, with a way to
open each run. Somebody moving between floors walks to the lift and is gone,
and turns up from the lift on the other one; the arrows beside **Back to the
building** go to the next floor up or down with a team on it.

People walk round anyone standing still, pass behind whoever is sitting at a
desk, and wait their turn where there's only room for one. If the wait goes on
they look for another way, and in the end squeeze past, so nobody is ever stuck:
two people meeting in a gap one tile wide do get by, the one later in the order
giving way.

Inside, a floor turns an eighth at a time: square on from each side, and
between each pair from the corner, the floor drawn as a diamond with its walls
standing up and everybody where they are. It zooms in whole steps so the pixels
stay square. How big it starts is the machine setting
`team-office-scale`, also on the Settings page as **Office size**: the smallest
and largest screen pixels per pixel of art, `1-2` unless you set it. Zoom goes
either side of it. On a display scaled to 125% a range of 1 to 2 only holds one
whole step, which is why zoom isn't limited to the range.

**Back to the building**, or Escape, takes you out again, to where you were.

## The lobby, the roof and the basements

**The lobby** has the entrance in the street-side glass, reception, and a
waiting room with a seat for everything that hasn't started yet: each scheduled
run and each task still to do is somebody on a sofa, and their card says what
it is and when. When a run starts, its lead walks in off the street and takes
the lift up. IT help has a desk in the band east of the lift.

**The roof** is where people go on a break. Somebody with nothing asked of them
for a minute leaves their floor by the lift and turns up here, wandering
between the benches and the edge. The minute stops a node between two steps
from bouncing up and down. Behind glass west of the lift there is a gym.

**The basements** hold the building's plumbing. The first has the mail room,
where every run's post is kept (what it was asked, and how it ended), and
storage, where what it delivered is shelved, and in its band a bike store and
showers. The second has the garbage room, which is the bin, and the server
room, which is the daemon.

## The rooms do things

Every room with a job is part of the dashboard. Click in one and a popup opens
with what it holds and what you can do about it:

| Room | Shows | Can do |
|---|---|---|
| Lead's office | the run's goal, what it has spent, how it's going | open the run, stop it |
| Meeting room | the questions and decisions waiting on you | open the run to answer them |
| Status board | the run in a line | open the run |
| Team rooms, open plan | who's working there and on what | open the run |
| Kitchen, lounge, roof | who is on a break, and why | - |
| Reception | who has arrived today | - |
| Waiting room, lobby screen | what is waiting, and what's scheduled next | stop a schedule |
| Mail room | each run's post: what it was asked, how it ended | open the run |
| Storage | what each run delivered | open the run |
| Garbage room | what is in the bin, and when each goes | bring one back, delete one for good |
| Server room | whether this is the daemon, and whether its schedules are held | hold or resume the schedules, restart the daemon |

Two rules hold throughout. None of these rooms does anything itself: every
button runs the same command you would type, through the same path as every
other button on the dashboard, so the page and the command line can't come to
disagree. And anything that changes or removes something asks first, by name.
Long things, like a report or a diff, open on the run's own page rather than in
a popup.

## Making it look like yours

The building is made from a **kit**, the parts, and **rules**, the decisions. A
set in the office folder can bring its own `kit.json`: tilesets for the floors
and walls, and pieces (desks, sofas, the lift, pigeonholes) each with a
footprint, where it can go and where people sit at it. A `rules.json` beside it
changes the decisions: the size of a floor, how high a storey is, which rooms
there are and how many, which partitions each may have, and which layouts to
use.

Everything is on one scale, the one the people are drawn to: a tile is three
quarters of a metre. A floor is 40 by 24 tiles, 30 by 18 metres, and a storey is
5 tiles, 3.75 metres floor to floor, so a facade bay or a material tile is 32
pixels wide and 160 high.

A room is furnished one piece for each of its tags, picked at random from the
pieces that have it, so give a fridge its own tag rather than `kitchen`, or it
will sometimes be the whole kitchen. A room's `extras` are what it takes as
well: the kitchen's are `fridge`, `cooler` and `kitchen-table`, the lounge's
`armchair`, `coffee-table` and `beanbag`. Each goes in if the set has a piece
for it and there's room, inside the room, off every seat and place to stand,
and never cutting a way through off. The built-in shapes have none of them.

A piece can give its `sides` as well: where in its picture it is seen with its
front facing each way, `n`, `e`, `s` and `w`. Turn the floor a quarter and a
desk that faced you shows its side, not the same picture on its side. A floor
turns once every picture piece on it has all four.

A piece's front is the side away from whoever sits at it. A desk's seat is on
its north side, so facing `s` its sitter looks at you across it and you see the
backs of the monitors; a visitor's chair facing `s` is seen from behind. Art
drawn screen side first gives its north picture as `s`, and the others turned
the same half circle. Floor and wall tiles need
nothing extra: a tileset already says which tile has which corners, so a turned
floor is drawn from the tiles whose corners match where the walls now run.

Each kind of room has a floor of its own, from the rules' `floors`: wood in the
lead office and the lounge, tiles in the kitchen, a darker walkway in the
corridor and the meeting rooms, carpet tiles where people work, stone in the
core and the lobby. `@floor`, `@lobby`, `@roof` and `@basement` say what
anything on that level that is in no room is floored with, decking on the roof
and concrete below ground. A floor is a kit material called `floor-` and its
name, a tile 128 pixels square that repeats every four cells; a cell is floored
by the smallest room over it that names one.

Where the kit has floors, walls stand up off them: a face about a metre high,
pale with a skirting for a solid wall and framed for glass, under a top raised
above it that joins the walls beside it. They're drawn in among the people and
furniture by where they meet the floor, so somebody behind one is hidden by it.
A kit with no floors is drawn as before, its walls flat from its tilesets.

A piece can say where it **suits**: which rooms (`*` for any) and levels, what
it is to the room (`main`, the piece that makes the room that room; `extra`;
`divider`, for marking a room off without a wall; `decor`; `clutter`), what its
back goes to (`wall`, `window` or `free`), and which pieces it is grouped `with`.
A room's rules can say whose it is (`scope`: the `floor`'s, shared, or each
`team`'s), where it belongs (`zone`: core, perimeter, interior, corner, entrance
or any), one for every so many people (`per-head`), what it should be `near` or
`away` from and how much that matters, from 1 to 10, and how it is reached
(`access`: corridor, open or through). The check holds all of these to the names
it knows, so a misspelt room isn't quietly ignored.

```json
"tech-armchair": { "suits": { "rooms": ["lounge", "waiting-room"], "role": "extra", "against": "free", "with": ["coffee-table"] } }
```

```
loadout config set team-office-set my-office
loadout team office check my-office
```

A kit can also bring **materials** for the neighbourhood: a pixel-art tile for
each of `facade-glass`, `facade-brick`, `facade-concrete`, `roof`, `road`,
`pavement`, `grass`, `canopy` and `trunk`, with how much of a surface one tile
covers (a bay's width and a storey's height, for a facade) and, if it likes, a
night version with the windows lit. Each tile is laid over every cell of the
surfaces it covers, following them as the view turns. A material with no tile is
drawn in plain colour with windows.

```json
"materials": {
  "facade-brick": { "picture": "brick.png", "size": [64, 160], "night": "brick-night.png" }
}
```

The tower's own outside can come from the kit as well, its `facade`: bay pieces
one tile wide and a storey high, placed `facade`, which each storey picks among
along its length; a `corner` for each end, mirrored on the right; `lobby` pieces
two storeys high and an `entrance` for the middle of the front, the long side
seen first, across the two middle bays if it is twice a bay's width; a
`basement`; and `windows`, where the glass is in a bay, so the lights and the
people in them are drawn in the window rather than across the whole bay. A
facade with a piece that has no picture isn't drawn at all, rather than half in
art and half in shapes.

```json
"facade": { "bays": ["tech-facade-1", "tech-facade-2"], "corner": "tech-corner",
            "windows": [[4, 12, 24, 68]] }
```

A kit can bring **people** too: a sprite sheet for each person, with their
standing, walking, sitting and typing frames from each of four sides, and
`skins`, which says who draws each role. A role can name several people, and
`worker` covers any role the kit doesn't name.

```json
"skins": { "project-lead": ["lead"], "worker": ["engineer", "analyst", "tester"] },
"sheets": { "analyst": { "piece": "cast-analyst.png", "frame": [96, 96], "anchor": [48, 76],
                         "gender": "woman", "lap": 58, "animations": { "idle_s": { "frames": [0] } } } }
```

`lap` is the row of a frame where a desk's top meets them when they sit. When the
floor is turned so somebody sits side-on to their desk, they're pulled up to it,
the desk is drawn over their legs, and everything above their lap is drawn again
on top, so their hands are on the keyboard rather than under the desk. Without a
lap, the desk covers them.

A sheet can add `sitdown_n`, `sitdown_e`, `sitdown_s` and `sitdown_w`, from
standing to sitting. Then somebody reaching their chair sits down into it, and
somebody leaving it gets up first, the same frames backwards, before taking a
step. Without them, people go from standing to sitting at once, as before.

A sheet's `sit` is somebody on an office chair, the one they bring to their
desk. A sofa, a bench or a meeting table's drawn chairs already have a seat, so
a sheet can add `sofa_n`, `sofa_e`, `sofa_s` and `sofa_w`: sitting with nothing
drawn under them. Somebody waiting on a sofa, or stopping at a meeting table
while they wander, sits in those, and doesn't sit down on to an office chair
first. Without them they sit on their own chair there too.

A sheet can also give the **face** in each frame that shows one: where the eyes
and mouth are (`faces`, by frame number) and the person's skin, brow and lip
colours (`face`). Then expressions are drawn over the face rather than drawn
again for every pose: focused while typing, tired when slumped, a frown when the
node has failed, surprise when it's waiting on you, and a smile on the way out.
Otherwise the face is left as drawn, with a few seconds of a passing smile or
yawn every half minute or so, at a different moment for each person. A face's
`traits` say what to leave alone: `glasses` and `eyes_closed` keep the eyes as
they are, `beard` keeps the beard, so only lips are added, and `grin` marks a
mouth that is teeth. From the side, an expression only ever adds a few pixels,
so it can't paint past the outline of a profile. With reduced motion on, faces
keep to what the node is doing and the passing moods stop.

Each node of a run is drawn by one of its role's people, chosen so nobody in a
run turns up twice until everybody on the list has, and the same node is the
same person every time, on its floor and on the roof. A sheet's `gender`, one of
`woman`, `man` or `nonbinary`, decides the name on that person's desk: a woman's
name for a woman, a man's for a man, and a name that could be anybody's for
somebody who is neither, because "Theo" over an older woman reads as a mistake.
A sheet that doesn't say keeps the names everybody had before. One consequence
worth knowing: the name now follows the kit, so two machines with different
kits can call the same node by different names.

To choose who plays a role rather than leave it to the office, name them with
`team-office-cast`, as role=person pairs:

```
loadout config set team-office-cast "project-lead=analyst, reviewer=tester"
```

The first node of that role in a run, by name, is drawn as that person, and
they're taken out of everybody else's list, so nobody turns up twice. Any more
nodes of the role are drawn from its list as usual, and a pair naming somebody
the kit doesn't have is ignored. The name on the desk still fits whoever it is.

`team office check` says whether a set can be used, names each thing wrong with
it, and lists any part the rules need that the kit doesn't have. Those are
drawn in the built-in shapes until it does. A set for others to use should leave
that list empty: inside, the office is meant to be pixel art throughout.

## What isn't finished

Stated plainly, so nobody finds out by surprise:

- **One office ships, the Tech one.** Loadout comes with it and draws the
  building from it unless you choose another set. The other themes are being
  made. The basement's front is still drawn in plain shapes.
- **Floors of art turn only once their pictures allow it.** A pack's pieces
  turn once they give all four sides. Tiles are picked by their corners, not
  turned, so shading drawn into a tile, a shadow along a wall's south face say,
  stays where it was drawn. A room drawn from one hand-made picture doesn't
  turn at all.
- **From a corner, pieces mostly show a straight side.** A piece shows the
  diagonal side its front turns towards where the set has that side; most of
  the Tech set's pieces and all of its people have only the four straight ones
  so far, so a desk seen from a corner is its front or its side, squared up.
  The floor's own pictures are the square view's, laid on the diamond.
- **Some rules are checked but not used yet.** `scope` and `access` are held to
  their words but the planner doesn't read them, and the lobby, roof and
  basements are still laid out by hand.
- **The drawing is checked by eye.** The layouts are tested, thousands of floors
  at a time, and so is everything the server says. What the page draws from
  them was looked at in a browser, not tested.

## Credit

The office's art pack, its people, tiles and objects, is generated with
[PixelLab](https://pixellab.ai).
