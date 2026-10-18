# OfficeView

The office is the classic dashboard's pixel-art building: one floor per team run, with a lobby, a roof and two basements. Its people are at their desks, in meetings, at the coffee machine or on the roof. Studio shows it as the **Office** view of the Teams page, beside the run board, using **the same engine** the classic dashboard draws with. OfficeView is Studio's frame round that engine. The engine draws; the frame gives it Studio's toolbar, a keyboard way round it, a focus ring and room for a panel.

## The engine

The office's drawing code moves out of `dashboard.html` into a file of its own, unchanged, and both dashboards use it. The classic page keeps getting it inline, so its security policy doesn't change. It offers a host page the `OfficeEngine` interface in `index.d.ts`. Three parts of that interface are new:

- `where()`: where you are (outside, lobby, roof, a basement, a floor), and what can be done from there;
- `targets()`: the things you can stop on, with where they are on screen;
- `activate(id)`: what a click on it does.

The rest (turn, zoom, levels, views, back) is what its buttons already do.

## What it shows

- **Where you are** at the left of the bar: "The building", or "Floor 6", with what's there under it.
- **The toolbar:**
  - Back to the building (Esc);
  - a level up and down (PgUp, PgDn);
  - outside views: Corner, Aerial, From the street;
  - turn left and right (Q, E);
  - zoom out and in (−, +);
  - **Office art and size**, which goes to Settings → Machine (`team-office-set`, `team-office-scale`, `team-office-cast`).

  Buttons that don't apply where you are are disabled, and the outside views show only outside.
- **The picture.** It keeps its own colours in every theme, because it is art, not text. The frame round it follows the theme.
- **The keys** under it, in words.

## Keyboard

- **Tab** into the picture. A ring lands on the first thing there, with its name and what it's doing in a tag beside it.
- **Arrow keys** move to the nearest thing that way: along the arrow first, then across it. **Home** and **End** go to the first and last.
- **Enter** or **Space** does what a click does. From outside, a floor goes in. Inside, a person or a room opens its panel.
- **Escape** closes the panel; pressed again, it goes back out to the building.
- **Q**, **E**, **+**, **−**, **PgUp** and **PgDn** do what their buttons do.
- The ring is the frame's, laid over the picture where the engine says the thing is, so the engine draws nothing new for it.

The mouse does what it always did, and a hover shows a dashed ring.

## Accessibility

It is usable by mouse and keyboard. What it says to a screen reader is kept short: the picture is a group named for where you are, and the thing the ring is on is announced politely. Reading the office in depth stays with the classic dashboard and Studio's own run board, which say the same things in words. Motion follows Studio's `motion()`: reduced or none gives the engine's still drawing, as the classic page does today.

## Do and don't

- Do keep the engine's drawing exactly as it is: Studio adds a frame, not a second office.
- Do ignore the office's keys while someone types in a field or a dialog.
- Don't show it in Minimal or Pure Focus. They are calm on purpose, and the run board is there.
