"""Draws docs/images/features.svg, the at-a-glance panel in the README.

Hand-placed rather than generated from anything the program knows, because it
is a poster and not a report: the wording is chosen to be read in two seconds,
and the ordering puts the reason somebody installs this first.

Run from the repository root:

    python build/render-features-svg.py
"""

import io
import os

BG = "#11141a"
CARD = "#181c24"
EDGE = "#252b36"
TITLE = "#f2f4f8"
BODY = "#9aa4b4"
ACCENT = "#e0a458"
ACCENT_2 = "#6fb3c4"

CARDS = [
    (
        "01",
        "Your repo stays yours",
        ["Instructions, rules, memory and session", "state live in one workspace — never", "in somebody else's diff."],
        ACCENT,
    ),
    (
        "02",
        "The right instructions",
        ["72 specialists built in. Picked from", "your repository and your task, and it", "tells you why before it launches."],
        ACCENT_2,
    ),
    (
        "03",
        "It remembers",
        ["Durable facts per project, loaded as", "an index rather than in full, and", "screened so a token never lands."],
        ACCENT,
    ),
    (
        "04",
        "A launcher, not a flag soup",
        ["Full-screen UI with a palette over", "every command, found by what it is", "for: 'undo' reaches backup restore."],
        ACCENT_2,
    ),
    (
        "05",
        "The agent can ask back",
        ["It reads its own instructions and", "records what it learns. Nothing that", "changes your machine is offered."],
        ACCENT,
    ),
    (
        "06",
        "You see the bill",
        ["Token accounting from transcripts you", "already have, with cache reads priced", "as cache reads."],
        ACCENT_2,
    ),
]

W, H = 900, 560
PAD = 28
GAP = 16
COLS = 2
CARD_W = (W - PAD * 2 - GAP * (COLS - 1)) // COLS
CARD_H = 128
TOP = 118


def escape(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def main():
    out = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" '
        f'viewBox="0 0 {W} {H}" role="img" '
        f'aria-label="What Loadout gives you, in six parts" '
        # Also on the root, because a sanitiser that drops the style block
        # would otherwise leave the whole poster in the default serif.
        f'font-family="-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,Arial,sans-serif">',
        '<defs><style>'
        '.t{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,Arial,sans-serif}'
        '</style></defs>',
        f'<rect width="{W}" height="{H}" rx="14" fill="{BG}"/>',
    ]

    # Masthead.
    out.append(
        f'<text class="t" x="{PAD}" y="56" font-size="27" font-weight="700" '
        f'fill="{TITLE}">Loadout</text>'
    )
    out.append(
        f'<text class="t" x="{PAD}" y="84" font-size="15" fill="{BODY}">'
        f'One place for everything your AI agents need, so none of it ends up in your repo.</text>'
    )
    out.append(
        f'<rect x="{PAD}" y="98" width="46" height="3" rx="1.5" fill="{ACCENT}"/>'
    )

    for index, (number, title, lines, accent) in enumerate(CARDS):
        col = index % COLS
        row = index // COLS
        x = PAD + col * (CARD_W + GAP)
        y = TOP + row * (CARD_H + GAP)

        out.append(
            f'<rect x="{x}" y="{y}" width="{CARD_W}" height="{CARD_H}" rx="10" '
            f'fill="{CARD}" stroke="{EDGE}"/>'
        )
        # A rule in the card's own colour, so the eye can group them.
        out.append(
            f'<rect x="{x}" y="{y}" width="3" height="{CARD_H}" rx="1.5" fill="{accent}"/>'
        )
        out.append(
            f'<text class="t" x="{x + 22}" y="{y + 32}" font-size="11" font-weight="700" '
            f'letter-spacing="1.4" fill="{accent}">{number}</text>'
        )
        out.append(
            f'<text class="t" x="{x + 22}" y="{y + 58}" font-size="17" font-weight="600" '
            f'fill="{TITLE}">{escape(title)}</text>'
        )

        for line_index, line in enumerate(lines):
            out.append(
                f'<text class="t" x="{x + 22}" y="{y + 82 + line_index * 18}" '
                f'font-size="13" fill="{BODY}">{escape(line)}</text>'
            )

    footer = H - 34
    out.append(
        f'<rect x="{PAD}" y="{footer - 22}" width="{W - PAD * 2}" height="1" fill="{EDGE}"/>'
    )
    out.append(
        f'<text class="t" x="{PAD}" y="{footer}" font-size="13" fill="{BODY}">'
        f'Windows · Linux · macOS, natively   Claude Code and Codex '
        f'  every command takes --json   preview, snapshot, undo</text>'
    )

    out.append("</svg>")

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target = os.path.join(root, "docs", "images", "features.svg")

    with io.open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(out) + "\n")

    print("wrote", target)


if __name__ == "__main__":
    main()
