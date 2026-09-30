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

Each running run gets a floor, lowest first. The building is never shorter than
ten floors and grows past that when it has to. A team too big for one floor
takes the floor above as well, and gives it back once it has shrunk and stayed
small for a few minutes, so a team hovering at the edge doesn't flicker between
one floor and two. When a run finishes, its floor goes dark and stays that way
for an hour, so you can still go and look, then it's freed.

A floor is laid out for its run, from the run's own identifier: the same run
gets the same floor every time, and two runs get different ones. Every floor
has the lead's office in a corner, meeting rooms, a kitchen, the status board,
and the lift, stairs and toilets in the core, in the same place on every level
so the lift lines up through the tower. Some floors are open plan; others have
a corridor with team rooms off it, divided by walls, glass, low screens or
planters. Somebody moving between floors walks to the lift and is gone, and
turns up from the lift on the other one.

People walk round anyone standing still, pass behind whoever is sitting at a
desk, and wait their turn where there's only room for one. If the wait goes on
they look for another way, and in the end squeeze past, so nobody is ever stuck:
two people meeting in a gap one tile wide do get by, the one later in the order
giving way.

Inside, a floor turns a quarter at a time, like the tower does, and zooms in
whole steps so the pixels stay square. How big it starts is the machine setting
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
the lift up.

**The roof** is where people go on a break. Somebody with nothing asked of them
for a minute leaves their floor by the lift and turns up here, wandering
between the benches and the edge. The minute stops a node between two steps
from bouncing up and down.

**The basements** hold the building's plumbing. The first has the mail room,
where every run's post is kept (what it was asked, and how it ended), and
storage, where what it delivered is shelved. The second has the garbage room,
which is the bin, and the server room, which is the daemon.

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
| Waiting room, lobby screen | what is waiting, and what's scheduled next | open the waiting list |
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
changes the decisions: the size of a floor, which rooms there are and how many,
which partitions each may have, and which layouts to use.

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
  "facade-brick": { "picture": "brick.png", "size": [64, 96], "night": "brick-night.png" }
}
```

`team office check` says whether a set can be used, names each thing wrong with
it, and lists any part the rules need that the kit doesn't have. Those are
drawn in the built-in shapes until it does. A set for others to use should leave
that list empty: inside, the office is meant to be pixel art throughout.

## What isn't finished

Stated plainly, so nobody finds out by surprise:

- **The built-in shapes are a stand-in.** Without a set, the inside of the
  building is drawn in flat coloured shapes. The pixel-art pack that replaces
  them is being made.
- **Art turns only once a pack brings every facing.** A floor drawn from a
  pack's pictures stays facing one way until the pack has its pieces from
  each side; floors drawn in the built-in shapes turn now.
- **The painted rooms and the separate Waiting area are still here.** They go
  once the building has its art.
- **The drawing is checked by eye.** The layouts are tested, thousands of floors
  at a time, and so is everything the server says. What the page draws from
  them was looked at in a browser, not tested.

## Credit

The office's art pack, its people, tiles and objects, is generated with
[PixelLab](https://pixellab.ai).
