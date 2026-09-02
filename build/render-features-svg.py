"""Draws docs/images/features.svg, the picture at the top of the README.

The flow and the parts in one drawing: your repository on the left, the agent
on the right, and everything Loadout manages hanging off the middle. Mermaid
draws this as boxes and arrows; this is the same thing drawn properly.

Run from the repository root:

    python build/render-features-svg.py
"""

import io
import math
import os

BG = "#0f1218"
PANEL = "#171c25"
EDGE = "#2a323f"
WIRE = "#3d4757"
TITLE = "#f4f6fa"
BODY = "#95a0b1"
DIM = "#6d7889"
AMBER = "#e0a458"
CYAN = "#6fb3c4"

FONT = "-apple-system,BlinkMacSystemFont,Segoe UI,Helvetica,Arial,sans-serif"
MONO = "Cascadia Mono,SFMono-Regular,Consolas,Menlo,monospace"

W, H = 1180, 648
CX, CY = 590, 336
HUB_R = 94

PILL_W, PILL_H = 230, 62
TOP_Y, BOTTOM_Y = 104, 516
COLUMNS = (320, 590, 860)

END_W, END_H = 168, 104

# x centre, y centre, label, caption
SATELLITES = [
    (COLUMNS[0], TOP_Y + PILL_H // 2, "Instructions", "72, picked for your task"),
    (COLUMNS[1], TOP_Y + PILL_H // 2, "Memory", "the facts worth keeping"),
    (COLUMNS[2], TOP_Y + PILL_H // 2, "Tokens", "what it costs, up front"),
    (COLUMNS[0], BOTTOM_Y + PILL_H // 2, "Your repos", "protect · drift · migrate"),
    (COLUMNS[1], BOTTOM_Y + PILL_H // 2, "Sessions", "resume, and hand over"),
    (COLUMNS[2], BOTTOM_Y + PILL_H // 2, "Launcher", "a UI over the same commands"),
]


def escape(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def main():
    out = []
    add = out.append

    add(
        '<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" '
        'viewBox="0 0 %d %d" role="img" font-family="%s" '
        'aria-label="Your repository feeds Loadout, which holds instructions, memory, '
        'token accounting, repo hygiene, sessions and the launcher, and starts your agent">'
        % (W, H, W, H, FONT)
    )
    add(
        '<defs>'
        '<radialGradient id="glow" cx="50%%" cy="50%%" r="50%%">'
        '<stop offset="0%%" stop-color="%s" stop-opacity="0.15"/>'
        '<stop offset="100%%" stop-color="%s" stop-opacity="0"/>'
        '</radialGradient>'
        '<marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" '
        'markerHeight="7" orient="auto-start-reverse">'
        '<path d="M 0 0 L 10 5 L 0 10 z" fill="%s"/></marker>'
        '<marker id="arrowAmber" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" '
        'markerHeight="7" orient="auto-start-reverse">'
        '<path d="M 0 0 L 10 5 L 0 10 z" fill="%s"/></marker>'
        '</defs>' % (AMBER, AMBER, WIRE, AMBER)
    )
    add('<rect width="%d" height="%d" rx="14" fill="%s"/>' % (W, H, BG))

    add(
        '<text x="%d" y="48" text-anchor="middle" font-size="25" font-weight="700" '
        'fill="%s">Everything in one place, and out of your repo</text>' % (W // 2, TITLE)
    )

    add('<circle cx="%d" cy="%d" r="270" fill="url(#glow)"/>' % (CX, CY))

    # --- wires first, so everything else sits on top ------------------------
    for sx, sy, _, _ in SATELLITES:
        angle = math.atan2(CY - sy, sx - CX)
        x1 = CX + HUB_R * math.cos(angle)
        y1 = CY - HUB_R * math.sin(angle)
        y2 = sy + (PILL_H / 2 if sy < CY else -PILL_H / 2)
        add(
            '<line x1="%.1f" y1="%.1f" x2="%d" y2="%.0f" stroke="%s" stroke-width="1.4"/>'
            % (x1, y1, sx, y2, WIRE)
        )
        add('<circle cx="%d" cy="%.0f" r="3" fill="%s"/>' % (sx, y2, WIRE))

    # --- the two ends -------------------------------------------------------
    def end_panel(x, title, lines):
        y = CY - END_H // 2
        add(
            '<rect x="%d" y="%d" width="%d" height="%d" rx="11" fill="%s" stroke="%s"/>'
            % (x, y, END_W, END_H, PANEL, EDGE)
        )
        add('<rect x="%d" y="%d" width="3" height="%d" rx="1.5" fill="%s"/>' % (x, y, END_H, CYAN))
        add(
            '<text x="%d" y="%d" text-anchor="middle" font-size="16" font-weight="600" '
            'fill="%s">%s</text>' % (x + END_W // 2, y + 34, TITLE, escape(title))
        )
        for i, line in enumerate(lines):
            add(
                '<text x="%d" y="%d" text-anchor="middle" font-size="12" fill="%s">%s</text>'
                % (x + END_W // 2, y + 58 + i * 18, DIM, escape(line))
            )

    end_panel(28, "Your repository", ["source, and", "nothing else"])
    end_panel(W - 28 - END_W, "Your agent", ["Claude Code", "or Codex"])

    left_edge = 28 + END_W
    right_edge = W - 28 - END_W

    add(
        '<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="1.8" '
        'marker-end="url(#arrow)"/>' % (left_edge + 6, CY, CX - HUB_R - 10, CY, WIRE)
    )
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="11.5" fill="%s">scanned</text>'
        % ((left_edge + CX - HUB_R) // 2, CY - 13, DIM)
    )
    add(
        '<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="1.8" '
        'marker-end="url(#arrowAmber)"/>' % (CX + HUB_R + 10, CY, right_edge - 6, CY, AMBER)
    )
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="11.5" fill="%s">one context</text>'
        % ((CX + HUB_R + right_edge) // 2, CY - 13, AMBER)
    )

    # What the session sends back.
    add(
        '<path d="M %d %d V %d H %d" fill="none" stroke="%s" stroke-width="1.4" '
        'stroke-dasharray="4 4" marker-end="url(#arrow)"/>'
        % (right_edge + END_W // 2, CY + END_H // 2, H - 62, CX + 150, WIRE)
    )
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="11.5" fill="%s">'
        'what it learned goes back</text>' % (W - 300, H - 70, DIM)
    )

    # --- the parts ----------------------------------------------------------
    for cx, cy, label, caption in SATELLITES:
        x, y = cx - PILL_W // 2, cy - PILL_H // 2
        add(
            '<rect x="%d" y="%d" width="%d" height="%d" rx="9" fill="%s" stroke="%s"/>'
            % (x, y, PILL_W, PILL_H, PANEL, EDGE)
        )
        add(
            '<text x="%d" y="%d" text-anchor="middle" font-size="15" font-weight="600" '
            'fill="%s">%s</text>' % (cx, cy - 4, TITLE, escape(label))
        )
        add(
            '<text x="%d" y="%d" text-anchor="middle" font-size="11.5" fill="%s">%s</text>'
            % (cx, cy + 15, BODY, escape(caption))
        )

    # --- the hub ------------------------------------------------------------
    add(
        '<circle cx="%d" cy="%d" r="%d" fill="%s" stroke="%s" stroke-width="1.8"/>'
        % (CX, CY, HUB_R, PANEL, AMBER)
    )
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="24" font-weight="700" '
        'font-family="%s" fill="%s">loadout</text>' % (CX, CY - 12, MONO, TITLE)
    )
    add('<rect x="%d" y="%d" width="48" height="2" rx="1" fill="%s"/>' % (CX - 24, CY + 2, AMBER))
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="12.5" fill="%s">one workspace,</text>'
        % (CX, CY + 26, BODY)
    )
    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="12.5" fill="%s">'
        'versioned if you like</text>' % (CX, CY + 43, BODY)
    )

    add(
        '<text x="%d" y="%d" text-anchor="middle" font-size="12" fill="%s">'
        'Windows, Linux and macOS natively  ·  every command takes --json  ·  '
        'nothing changes without a preview and a snapshot</text>' % (W // 2, H - 24, DIM)
    )

    add("</svg>")

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target = os.path.join(root, "docs", "images", "features.svg")

    with io.open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(out) + "\n")

    print("wrote", target)


if __name__ == "__main__":
    main()
